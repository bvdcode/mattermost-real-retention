using System.Globalization;
using System.Net.Http.Json;
using Mattermost.RealRetention.Models;
using Microsoft.Extensions.Options;

namespace Mattermost.RealRetention.Services;

public sealed class MattermostNotifier(HttpClient client, IOptions<RetentionOptions> options,
    ILogger<MattermostNotifier> logger) : IRetentionNotifier
{
    public async Task NotifyAsync(RetentionReport report, CancellationToken cancellationToken = default)
    {
        // Successful empty runs remain available in logs and /status without notifying the channel.
        if (report.Status == "Completed" && report.CandidateRecordsCount == 0 &&
            report.ProcessedFilesCount == 0 && report.DeletedRecordsCount == 0 &&
            report.SkippedRecordsCount == 0 && report.FailedRecordsCount == 0 && string.IsNullOrEmpty(report.Error))
            return;

        var url = options.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Retention notification skipped: WEBHOOK_URL must be an absolute HTTP(S) URL.");
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { text = FormatReport(report) })
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                logger.LogInformation("Retention report accepted by Mattermost.");
            else
                logger.LogWarning("Retention notification failed with HTTP {statusCode}; cleanup results are unchanged.", (int)response.StatusCode);
        }
        catch (Exception exception)
        {
            // Neither exception messages nor response bodies are logged: they may contain the secret URL.
            logger.LogWarning("Retention notification failed ({errorType}); cleanup results are unchanged.", exception.GetType().Name);
        }
    }

    public static string FormatReport(RetentionReport report)
    {
        var status = report.Status switch
        {
            "Completed" => "завершена",
            "CompletedWithErrors" => "завершена с ошибками",
            "Cancelled" => "прервана",
            _ => "ошибка"
        };
        var lines = new List<string>
        {
            $"**Mattermost Retention — {status}**",
            report.DryRun ? "Режим: **dry-run — без удаления**" : "Режим: очистка",
            "",
            $"Проверено записей вложений: **{report.CandidateRecordsCount}**",
            $"Проверено файлов: **{report.ProcessedFilesCount}**",
            $"Удалено файлов: **{report.DeletedFilesCount}**",
            $"Удалено записей из БД: **{report.DeletedRecordsCount}**",
            $"Освобождено: **{FormatBytes(report.DeletedBytes)}**"
        };
        if (report.DryRun)
        {
            lines.Add($"Можно удалить файлов: **{report.WouldDeleteFilesCount}**");
            lines.Add($"Можно освободить: **{FormatBytes(report.WouldDeleteBytes)}**");
        }
        lines.Add($"Файлов уже нет на диске: **{report.MissingFilesCount}**");
        lines.Add($"Пропущено записей: **{report.SkippedRecordsCount}**");
        lines.Add($"Записей с ошибками: **{report.FailedRecordsCount}**");
        if (report.CompletedAt is { } completed)
        {
            var duration = completed - report.CreatedAt;
            lines.Add($"Время выполнения: **{Math.Max(0, duration.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)} с**");
        }
        if (report.Status is "Failed" or "CompletedWithErrors")
            lines.Add("Подробности ошибок — в логах контейнера. Оставшиеся записи будут проверены при следующем запуске.");
        return string.Join('\n', lines);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
