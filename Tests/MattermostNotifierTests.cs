using System.Net;
using System.Text.Json;
using Mattermost.RealRetention.Models;
using Mattermost.RealRetention.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mattermost.RealRetention.Tests;

public sealed class MattermostNotifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-url")]
    [InlineData("file:///tmp/webhook")]
    public async Task Missing_or_invalid_webhook_does_not_send(string? url)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        await Create(client, logger, url).NotifyAsync(new RetentionReport());
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Sends_aggregate_report_without_paths_or_error_details()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var report = new RetentionReport
        {
            Status = "Completed", CandidateRecordsCount = 3, DeletedRecordsCount = 1,
            Directory = "/private/storage", Error = "private-error-details",
            CreatedAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 10, 23, 12, 0, 2, DateTimeKind.Utc)
        };
        report.OnFileProcessed("private-file-name", 2 * 1024 * 1024, exists: true, dryRun: false);
        report.OnFileProcessed("private-missing-name", 0, exists: false, dryRun: false);
        await Create(client, logger).NotifyAsync(report);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("application/json", handler.MediaType);
        using var json = JsonDocument.Parse(handler.Body!);
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("Удалено файлов: **1**", text);
        Assert.Contains("Удалено записей из БД: **1**", text);
        Assert.Contains("Освобождено: **2 MiB**", text);
        Assert.Contains("Файлов уже нет на диске: **1**", text);
        Assert.Contains("Время выполнения: **2.0 с**", text);
        Assert.DoesNotContain("private", handler.Body!);
        Assert.DoesNotContain("example.test", handler.Body!);
        Assert.DoesNotContain("username", handler.Body!);
        Assert.DoesNotContain("channel", handler.Body!);
    }

    [Fact]
    public async Task Dry_run_does_not_claim_actual_deletion()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var report = new RetentionReport { DryRun = true, Status = "Completed" };
        report.OnFileProcessed("file", 1024 * 1024 * 1024L, true, true);
        await Create(client, new RecordingLogger()).NotifyAsync(report);
        using var json = JsonDocument.Parse(handler.Body!);
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("dry-run — без удаления", text);
        Assert.Contains("Удалено файлов: **0**", text);
        Assert.Contains("Освобождено: **0 B**", text);
        Assert.Contains("Можно удалить файлов: **1**", text);
        Assert.Contains("Можно освободить: **1 GiB**", text);
    }

    [Theory]
    [InlineData("Failed", "ошибка")]
    [InlineData("CompletedWithErrors", "завершена с ошибками")]
    [InlineData("Cancelled", "прервана")]
    public void Non_success_status_is_visible(string status, string expected)
    {
        Assert.Contains(expected, MattermostNotifier.FormatReport(new RetentionReport { Status = status }));
    }

    [Fact]
    public async Task Http_failure_is_non_fatal_and_response_body_is_not_logged()
    {
        using var handler = new RecordingHandler { Status = HttpStatusCode.InternalServerError };
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        await Create(client, logger).NotifyAsync(new RetentionReport());
        Assert.Contains(logger.Messages, message => message.Contains("500"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret") || message.Contains("example.test"));
    }

    [Fact]
    public async Task Network_exception_does_not_leak_webhook_or_fail_the_job()
    {
        using var handler = new RecordingHandler { Error = new HttpRequestException("https://example.test/hooks/secret") };
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        await Create(client, logger).NotifyAsync(new RetentionReport());
        Assert.Contains(logger.Messages, message => message.Contains("HttpRequestException"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret") || message.Contains("example.test"));
    }

    [Fact]
    public async Task Timeout_is_non_fatal_and_request_is_not_retried()
    {
        using var handler = new RecordingHandler { WaitForCancellation = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var logger = new RecordingLogger();
        await Create(client, logger).NotifyAsync(new RetentionReport());
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(logger.Messages, message => message.Contains("CanceledException"));
    }

    private static MattermostNotifier Create(HttpClient client, RecordingLogger logger,
        string? url = "https://example.test/hooks/secret") => new(client,
            Options.Create(new RetentionOptions { WebhookUrl = url }), logger);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? Body { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? MediaType { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Exception? Error { get; init; }
        public bool WaitForCancellation { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Method = request.Method;
            MediaType = request.Content.Headers.ContentType?.MediaType;
            if (Error != null)
                throw Error;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent("secret-response-details") };
        }
    }

    private sealed class RecordingLogger : ILogger<MattermostNotifier>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
