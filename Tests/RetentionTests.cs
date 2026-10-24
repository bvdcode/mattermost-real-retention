using System.Data.Common;
using Mattermost.RealRetention.Database;
using Mattermost.RealRetention.Models;
using Mattermost.RealRetention.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Mattermost.RealRetention.Tests;

public sealed class RetentionTests : IAsyncLifetime
{
    private readonly string schema = "retention_test_" + Guid.NewGuid().ToString("N");
    private readonly string directory = Path.Combine(Path.GetTempPath(), "retention-test-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private readonly ReportService reports = new();
    private readonly RecordingNotifier notifier = new();

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("RETENTION_TEST_DATABASE")
            ?? throw new InvalidOperationException("Set RETENTION_TEST_DATABASE to an isolated PostgreSQL test database. See README.md.");
        var builder = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema, Pooling = false };
        connectionString = builder.ConnectionString;
        await SqlAsync($"CREATE SCHEMA {schema}");
        await SqlAsync("""
            CREATE TABLE posts (id varchar(26) PRIMARY KEY, deleteat bigint NOT NULL DEFAULT 0);
            CREATE TABLE fileinfo (
                id varchar(26) PRIMARY KEY, deleteat bigint NOT NULL DEFAULT 0,
                path text NOT NULL DEFAULT '', thumbnailpath text NOT NULL DEFAULT '',
                previewpath text NOT NULL DEFAULT '', postid varchar(26) NOT NULL DEFAULT '');
            CREATE INDEX ON fileinfo(postid);
            """);
        Directory.CreateDirectory(Path.Combine(directory, "20261023"));
    }

    public async Task DisposeAsync()
    {
        if (connectionString != null)
            await SqlAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Deletes_original_thumbnail_and_preview_after_confirmed_post_deletion()
    {
        await PostAsync("deleted", deleted: true);
        var original = MakeFile("original");
        var thumbnail = MakeFile("thumb");
        var preview = MakeFile("preview");
        await FileAsync("file", "deleted", original, thumbnail, preview);

        using var db = Context();
        var report = await Service(db).RunAsync();

        Assert.Equal(3, report.DeletedFilesCount);
        Assert.Equal(1, report.DeletedRecordsCount);
        Assert.Equal("Completed", report.Status);
        Assert.Same(report, Assert.Single(notifier.Reports));
        Assert.NotNull(report.CompletedAt);
        Assert.False(File.Exists(Path.Combine(directory, original)));
        Assert.False(File.Exists(Path.Combine(directory, thumbnail)));
        Assert.False(File.Exists(Path.Combine(directory, preview)));
        Assert.Equal(0, await db.Files.CountAsync());
        Assert.Equal(1, await db.Posts.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Dry_run_preserves_files_and_metadata_and_reports_would_delete()
    {
        await PostAsync("deleted", true);
        var path = MakeFile("dry-run");
        await FileAsync("file", "deleted", path);
        using var db = Context();

        var report = await Service(db, dryRun: true).RunAsync();

        Assert.True(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Equal(0, report.DeletedFilesCount);
        Assert.Equal(0, report.DeletedRecordsCount);
        Assert.Equal(1, report.WouldDeleteFilesCount);
        Assert.False(Assert.Single(report.ProcessedFiles).IsDeleted);
    }

    [Fact]
    public async Task Preserves_active_missing_and_unattached_posts_and_unknown_disk_files()
    {
        await PostAsync("active", false);
        var active = MakeFile("active");
        var missing = MakeFile("missing-post");
        var upload = MakeFile("upload");
        var unknown = MakeFile("unknown");
        await FileAsync("active-file", "active", active);
        await FileAsync("missing-file", "missing", missing);
        await FileAsync("upload-file", "", upload);
        using var db = Context();

        var report = await Service(db).RunAsync();

        Assert.Equal(0, report.CandidateRecordsCount);
        Assert.Equal(3, await db.Files.CountAsync());
        Assert.All(new[] { active, missing, upload, unknown }, path => Assert.True(File.Exists(Path.Combine(directory, path))));
    }

    [Fact]
    public async Task Explicitly_deleted_file_can_be_removed_from_an_active_post()
    {
        await PostAsync("active", false);
        var path = MakeFile("deleted-file");
        await FileAsync("file", "active", path, deleted: true);
        using var db = Context();
        Assert.Equal(1, (await Service(db).RunAsync()).DeletedFilesCount);
        Assert.Equal(1, await db.Posts.CountAsync());
    }

    [Fact]
    public async Task Handles_missing_and_duplicate_paths_and_repeated_runs()
    {
        await PostAsync("deleted", true);
        var path = MakeFile("same-file");
        await FileAsync("file", "deleted", path, path, "20261023/already-gone");
        using var db = Context();
        var report = await Service(db).RunAsync();
        Assert.Equal(1, report.DeletedFilesCount);
        Assert.Equal(1, report.MissingFilesCount);
        Assert.Equal(0, (await Service(db).RunAsync()).CandidateRecordsCount);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("20261023/../outside")]
    public async Task Invalid_preview_preserves_the_original_and_metadata(string invalidPath)
    {
        await PostAsync("deleted", true);
        var original = MakeFile("original");
        await FileAsync("file", "deleted", original, preview: invalidPath);
        using var db = Context();
        var report = await Service(db).RunAsync();
        Assert.True(File.Exists(Path.Combine(directory, original)));
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Equal(1, report.FailedRecordsCount);
        Assert.Equal("CompletedWithErrors", report.Status);
    }

    [Fact]
    public async Task Rejects_symbolic_links_without_deleting_the_target()
    {
        await PostAsync("deleted", true);
        var target = MakeFile("target");
        File.CreateSymbolicLink(Path.Combine(directory, "20261023", "link"), Path.Combine(directory, target));
        await FileAsync("file", "deleted", "20261023/link");
        using var db = Context();
        Assert.Equal(1, (await Service(db).RunAsync()).FailedRecordsCount);
        Assert.True(File.Exists(Path.Combine(directory, target)));
        Assert.Equal(1, await db.Files.CountAsync());
    }

    [Fact]
    public async Task Preserves_files_referenced_by_another_attachment()
    {
        await PostAsync("deleted", true);
        await PostAsync("active", false);
        var path = MakeFile("shared");
        await FileAsync("deleted-file", "deleted", path);
        await FileAsync("active-file", "active", path);
        using var db = Context();
        Assert.Equal(1, (await Service(db).RunAsync()).SkippedRecordsCount);
        Assert.True(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(2, await db.Files.CountAsync());
    }

    [Fact]
    public async Task Keyset_batches_progress_past_failed_records_and_bound_report_memory()
    {
        await PostAsync("deleted", true);
        await FileAsync("0000", "deleted", "../invalid");
        for (var i = 1; i <= 125; i++)
            await FileAsync(i.ToString("D4"), "deleted", MakeFile(i.ToString()));
        using var db = Context();
        var report = await Service(db, batchSize: 7).RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(126, report.CandidateRecordsCount);
        Assert.Equal(125, report.DeletedRecordsCount);
        Assert.Equal(125, report.DeletedFilesCount);
        Assert.Equal(1, report.FailedRecordsCount);
        Assert.Equal(RetentionReport.DetailLimit, report.ProcessedFiles.Count);
        Assert.True(report.DetailsTruncated);
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task More_than_a_million_active_posts_does_not_affect_deletion_decisions()
    {
        await SqlAsync("INSERT INTO posts(id) SELECT lpad(i::text, 26, '0') FROM generate_series(1, 1000001) AS i");
        await PostAsync("deleted", true);
        var path = MakeFile("millionth-active");
        await FileAsync("active-file", "00000000000000000001000001", path);
        await FileAsync("deleted-file", "deleted", MakeFile("deleted"));
        using var db = Context();
        var report = await Service(db).RunAsync();
        Assert.Equal(1, report.CandidateRecordsCount);
        Assert.Equal(1, report.DeletedRecordsCount);
        Assert.True(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Permission_failure_keeps_metadata_and_retry_finishes_partial_deletion()
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Filesystem permission tests require Linux.");
        await PostAsync("deleted", true);
        var original = MakeFile("original");
        var blocked = Path.Combine(directory, "20261023", "blocked");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "thumb"), "thumbnail");
        await FileAsync("a-failed", "deleted", original, "20261023/blocked/thumb");
        await FileAsync("b-success", "deleted", MakeFile("other"));
        using var db = Context();
        File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var report = await Service(db).RunAsync();
            Assert.Equal(1, report.FailedRecordsCount);
            Assert.Equal(1, report.DeletedRecordsCount);
            Assert.False(File.Exists(Path.Combine(directory, original)));
            Assert.True(File.Exists(Path.Combine(blocked, "thumb")));
            Assert.Equal(1, await db.Files.CountAsync());
        }
        finally
        {
            File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var retry = await Service(db).RunAsync();
        Assert.Equal(1, retry.MissingFilesCount);
        Assert.Equal(1, retry.DeletedFilesCount);
        Assert.Equal(1, retry.DeletedRecordsCount);
        Assert.Equal(0, await db.Files.CountAsync());
    }

    [Fact]
    public async Task Database_failure_after_disk_deletion_is_recoverable()
    {
        await PostAsync("deleted", true);
        var path = MakeFile("original");
        await FileAsync("file", "deleted", path);
        await SqlAsync("""
            CREATE FUNCTION reject_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Simulated database failure'; END $$;
            CREATE TRIGGER reject_delete BEFORE DELETE ON fileinfo FOR EACH ROW EXECUTE FUNCTION reject_delete();
            """);
        using var db = Context();
        await Assert.ThrowsAsync<PostgresException>(() => Service(db).RunAsync());
        Assert.False(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Equal("Failed", Assert.Single(reports.GetReports()).Status);
        Assert.Equal("Failed", Assert.Single(notifier.Reports).Status);
        await SqlAsync("DROP TRIGGER reject_delete ON fileinfo");
        var retry = await Service(db).RunAsync();
        Assert.Equal(1, retry.MissingFilesCount);
        Assert.Equal(1, retry.DeletedRecordsCount);
        Assert.Equal(0, await db.Files.CountAsync());
    }

    [Fact]
    public async Task Rechecks_post_state_after_waiting_for_a_concurrent_update()
    {
        await PostAsync("post", true);
        var path = MakeFile("restored");
        await FileAsync("file", "post", path);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT id FROM fileinfo WHERE id='file' FOR UPDATE", connection, transaction))
            await command.ExecuteScalarAsync();
        var interceptor = new LockQuerySignal();
        using var db = Context(interceptor);
        var run = Service(db).RunAsync();
        await interceptor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var command = new NpgsqlCommand("UPDATE posts SET deleteat=0 WHERE id='post'", connection, transaction))
            await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        var report = await run;
        Assert.Equal(1, report.SkippedRecordsCount);
        Assert.True(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(1, await db.Files.CountAsync());
    }

    [Fact]
    public async Task Concurrent_instances_do_not_delete_the_same_attachment_twice()
    {
        await PostAsync("post", true);
        await FileAsync("file", "post", MakeFile("original"));
        using var first = Context();
        using var second = Context();
        var results = await Task.WhenAll(Service(first).RunAsync(), Service(second).RunAsync());
        Assert.Equal(1, results.Sum(report => report.DeletedRecordsCount));
        Assert.Equal(1, results.Sum(report => report.DeletedFilesCount));
        Assert.Equal(0, await first.Files.CountAsync());
    }

    [Fact]
    public async Task Cancellation_keeps_metadata_and_records_an_interrupted_run()
    {
        await PostAsync("post", true);
        var path = MakeFile("original");
        await FileAsync("file", "post", path);
        using var db = Context();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var service = new RetentionService(db, Options.Create(new RetentionOptions
        {
            DryRun = false, DataDirectory = directory, DelayBetweenFilesInMs = 10000
        }), reports, notifier, NullLogger<RetentionService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cancellation.Token));
        Assert.True(File.Exists(Path.Combine(directory, path)));
        Assert.Equal(1, await db.Files.CountAsync());
        Assert.Equal("Cancelled", Assert.Single(reports.GetReports()).Status);
        Assert.Equal("Cancelled", Assert.Single(notifier.Reports).Status);
    }

    [Fact]
    public async Task Empty_mount_fails_before_deleting_metadata()
    {
        await FileAsync("file", "", "20261023/missing", deleted: true);
        Directory.Delete(Path.Combine(directory, "20261023"));
        using var db = Context();
        await Assert.ThrowsAsync<IOException>(() => Service(db).RunAsync());
        Assert.Equal(1, await db.Files.CountAsync());
    }

    [Fact]
    public void Report_history_is_bounded()
    {
        for (var i = 0; i < 50; i++)
            reports.AddReport(new RetentionReport { CandidateRecordsCount = i });
        Assert.Equal(ReportService.HistoryLimit, reports.GetReports().Count);
        Assert.Equal(40, reports.GetReports()[0].CandidateRecordsCount);
        Assert.Equal(49, reports.GetReports()[^1].CandidateRecordsCount);
    }

    private AppDbContext Context(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        if (interceptor != null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    private RetentionService Service(AppDbContext db, bool dryRun = false, int batchSize = 2) => new(db,
        Options.Create(new RetentionOptions { DataDirectory = directory, DryRun = dryRun, BatchSize = batchSize }),
        reports, notifier, NullLogger<RetentionService>.Instance);

    private string MakeFile(string name)
    {
        var path = "20261023/" + name;
        File.WriteAllText(Path.Combine(directory, path), "attachment content");
        return path;
    }

    private Task PostAsync(string id, bool deleted) => SqlAsync(
        "INSERT INTO posts(id,deleteat) VALUES ($1,$2)", id, deleted ? 123L : 0L);

    private Task FileAsync(string id, string post, string path, string thumbnail = "", string preview = "", bool deleted = false) => SqlAsync(
        "INSERT INTO fileinfo(id,postid,path,thumbnailpath,previewpath,deleteat) VALUES ($1,$2,$3,$4,$5,$6)",
        id, post, path, thumbnail, preview, deleted ? 123L : 0L);

    private async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        foreach (var value in parameters)
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingNotifier : IRetentionNotifier
    {
        public System.Collections.Concurrent.ConcurrentQueue<RetentionReport> Reports { get; } = new();

        public Task NotifyAsync(RetentionReport report, CancellationToken cancellationToken = default)
        {
            Reports.Enqueue(report);
            return Task.CompletedTask;
        }
    }

    private sealed class LockQuerySignal : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
