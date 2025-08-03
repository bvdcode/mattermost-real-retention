using Quartz;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Mattermost.RealRetention.Models;
using EasyExtensions.Quartz.Attributes;
using Mattermost.RealRetention.Services;
using Mattermost.RealRetention.Database;

namespace Mattermost.RealRetention.Jobs
{
    [JobTrigger(days: 1)]
    public class RetentionJob(AppDbContext _dbContext, ILogger<RetentionJob> _logger,
        IConfiguration configuration, ReportService _reports) : IJob
    {
        private const int defaultDelay = 0;
        private const string folder = "/mattermost/data/";

        public async Task Execute(IJobExecutionContext context)
        {
            RetentionReport report = new();
            _reports.AddReport(report);
            bool dryRun = configuration.GetValue("DryRun", true);
            int delay = configuration.GetValue("DelayBetweenFilesInMs", defaultDelay);
            if (delay < 0)
            {
                _logger.LogWarning("Delay between files is set to a negative value ({delay} ms), using default {defaultDelay} ms instead.", delay, defaultDelay);
                delay = defaultDelay;
            }
            _logger.LogInformation("Starting retention job with delay {delay} ms and dry run mode {dryRun}.", delay, dryRun);
            report.Delay = delay;
            report.CreatedAt = DateTime.UtcNow;
            report.DryRun = dryRun;

            DirectoryInfo di = new(folder);
            report.Directory = di.FullName;
            if (!di.Exists)
            {
                throw new DirectoryNotFoundException($"The directory {folder} does not exist.");
            }
            int counter = 0;

            var dateDirectories = di.GetDirectories()
                .Where(d =>
                    d.Name.Length == 8 // Ensure the directory name is 8 characters long (YYYYMMDD format)
                    && DateTime.TryParseExact(d.Name, "yyyyMMdd", null, DateTimeStyles.None, out _)
                )
                .ToList();
            report.FoldersCount = dateDirectories.Count;

            var files = dateDirectories
                .SelectMany(d => d.GetFiles("*", SearchOption.AllDirectories))
                .ToArray();
            report.TotalFilesCount = files.Length;

            _logger.LogInformation(
                "Found {filesLength} files in {directoryCount} date directories in {folder}.",
                files.Length,
                dateDirectories.Count,
                folder
            );

            _logger.LogInformation("Preloading database files for performance optimization...");
            await _dbContext.Files.ToListAsync(context.CancellationToken);

            foreach (var file in files)
            {
                await Task.Delay(delay);
                string relativePath = file.FullName.Replace(folder, string.Empty);
                _logger.LogDebug("Processing file {fileName}.", relativePath);
                var fileInfo = _dbContext.Files.FirstOrDefault(f =>
                    f.Path == relativePath
                    || f.ThumbnailPath == relativePath
                    || f.PreviewPath == relativePath
                );
                if (fileInfo == null)
                {
                    _logger.LogWarning("File {fileName} not found in the database - deleting from filesystem.", relativePath);
                    if (dryRun)
                    {
                        _logger.LogInformation("Dry run enabled, skipping actual deletion.");
                    }
                    else
                    {
                        file.Delete();
                    }
                    counter++;
                    report.OnFileProcessed(relativePath, file.Length, deleted: true, "File not found in database.");
                    continue;
                }
                var postExists = _dbContext.Posts.Any(p =>
                    p.Id == fileInfo.PostId && p.DeletedAt == 0
                );
                if (postExists && fileInfo.DeletedAt == 0)
                {
                    _logger.LogInformation(
                        "File {fileName} with ID {fileId} is associated with an active post, skipping deletion.",
                        relativePath,
                        fileInfo.Id
                    );
                    report.OnFileProcessed(relativePath, file.Length, deleted: false, "File is associated with an active post.");
                    continue;
                }
                else
                {
                    string result = fileInfo.DeletedAt > 0
                        ? "is marked deleted"
                        : "is associated with a deleted post";
                    _logger.LogWarning("File {fileName} with ID {fileId} {result} - deleting.", relativePath, fileInfo.Id, result);
                    if (dryRun)
                    {
                        _logger.LogInformation("Dry run enabled, skipping actual deletion.");
                    }
                    else
                    {
                        _dbContext.Files.Remove(fileInfo);
                        await _dbContext.SaveChangesAsync();
                        file.Delete();
                    }
                    report.OnFileProcessed(relativePath, file.Length, deleted: true, $"File {result}.");
                    counter++;
                }
            }

            _logger.LogInformation("Retention job completed. {counter} files deleted, {total} files total.", counter, files.Length);
        }
    }
}
