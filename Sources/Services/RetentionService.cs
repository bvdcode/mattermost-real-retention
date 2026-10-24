using System.ComponentModel.DataAnnotations;
using Mattermost.RealRetention.Database;
using Mattermost.RealRetention.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mattermost.RealRetention.Services;

public sealed class RetentionService(
    AppDbContext database,
    IOptions<RetentionOptions> options,
    ReportService reports,
    IRetentionNotifier notifier,
    ILogger<RetentionService> logger)
{
    public async Task<RetentionReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var report = new RetentionReport
        {
            DryRun = settings.DryRun,
            Delay = settings.DelayBetweenFilesInMs,
            Directory = Path.GetFullPath(settings.DataDirectory)
        };

        try
        {
            Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
            var storage = new LocalFileStorage(report.Directory);
            storage.ValidateRoot(requireNonEmpty: !settings.DryRun);
            logger.LogInformation("Starting retention. DryRun={dryRun}, batch size={batchSize}, directory={directory}.",
                settings.DryRun, settings.BatchSize, report.Directory);

            string? cursor = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Positive evidence of deletion only. Missing posts and unattached uploads are not garbage.
                var candidates = database.Files.AsNoTracking().Where(file => file.DeletedAt > 0 ||
                    database.Posts.Any(post => post.Id == file.PostId && post.DeletedAt > 0));
                if (cursor != null)
                    candidates = candidates.Where(file => string.Compare(file.Id, cursor) > 0);

                var ids = await candidates.OrderBy(file => file.Id).Select(file => file.Id)
                    .Take(settings.BatchSize).ToListAsync(cancellationToken);
                if (ids.Count == 0)
                    break;

                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    report.CandidateRecordsCount++;
                    try
                    {
                        await ProcessRecordAsync(id, storage, settings, report, cancellationToken);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        // Keep the metadata for a later retry, including when only some files were removed.
                        report.FailedRecordsCount++;
                        logger.LogError(exception, "Could not clean file record {fileId}; keeping its metadata for retry.", id);
                    }
                }

                cursor = ids[^1];
                logger.LogInformation("Retention progress: {candidates} records checked, {deleted} files deleted, {wouldDelete} would delete, {errors} errors.",
                    report.CandidateRecordsCount, report.DeletedFilesCount, report.WouldDeleteFilesCount, report.FailedRecordsCount);
            }

            report.Status = report.FailedRecordsCount == 0 ? "Completed" : "CompletedWithErrors";
            logger.LogInformation("Retention {status}: {deleted} files deleted ({bytes} bytes), {wouldDelete} would delete, {errors} failed records.",
                report.Status, report.DeletedFilesCount, report.DeletedBytes, report.WouldDeleteFilesCount, report.FailedRecordsCount);
            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.Status = "Cancelled";
            throw;
        }
        catch (Exception exception)
        {
            report.Status = "Failed";
            report.Error = exception.GetType().Name;
            logger.LogError(exception, "Retention failed; the next run can resume from the remaining metadata.");
            throw;
        }
        finally
        {
            report.CompletedAt = DateTime.UtcNow;
            reports.AddReport(report);
            // Give the bounded notification request a chance to report cancellation too.
            await notifier.NotifyAsync(report);
        }
    }

    private async Task ProcessRecordAsync(string id, LocalFileStorage storage, RetentionOptions settings,
        RetentionReport report, CancellationToken cancellationToken)
    {
        // A small transaction per attachment prevents its metadata or post being restored/changed
        // between checking the deletion flag and removing files. No transaction spans a whole batch.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var fileQuery = settings.DryRun
            ? database.Files.Where(file => file.Id == id)
            : database.Files.FromSqlInterpolated($"SELECT id, deleteat, path, thumbnailpath, previewpath, postid FROM fileinfo WHERE id = {id} FOR UPDATE");
        var file = (await fileQuery.AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
        if (file == null)
        {
            report.SkippedRecordsCount++;
            return;
        }

        if (file.DeletedAt <= 0)
        {
            var postQuery = settings.DryRun
                ? database.Posts.Where(post => post.Id == file.PostId)
                : database.Posts.FromSqlInterpolated($"SELECT id, deleteat FROM posts WHERE id = {file.PostId} FOR SHARE");
            var post = (await postQuery.AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
            if (post == null || post.DeletedAt <= 0)
            {
                report.SkippedRecordsCount++;
                return;
            }
        }

        var paths = new[] { file.Path, file.ThumbnailPath, file.PreviewPath }
            .Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal).ToArray();
        // Do not remove a physical file if another attachment record still references it.
        if (await database.Files.AsNoTracking().AnyAsync(other => other.Id != id &&
            (paths.Contains(other.Path) || paths.Contains(other.ThumbnailPath) || paths.Contains(other.PreviewPath)), cancellationToken))
        {
            report.SkippedRecordsCount++;
            logger.LogWarning("Skipping file record {fileId}: a path is shared with another attachment.", id);
            return;
        }

        // Validate every path before touching any file belonging to this record.
        var resolvedPaths = paths.Select(storage.Resolve).ToArray();
        for (var index = 0; index < paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.DelayBetweenFilesInMs > 0)
                await Task.Delay(settings.DelayBetweenFilesInMs, cancellationToken);
            var (exists, length) = storage.Process(resolvedPaths[index], settings.DryRun);
            report.OnFileProcessed(paths[index], length, exists, settings.DryRun);
        }

        if (!settings.DryRun)
        {
            // Missing files are already cleaned. Delete metadata only after ALL paths succeeded.
            await database.Files.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            report.DeletedRecordsCount++;
        }
    }
}
