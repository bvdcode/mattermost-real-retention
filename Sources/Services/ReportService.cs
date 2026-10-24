using Mattermost.RealRetention.Models;

namespace Mattermost.RealRetention.Services;

public sealed class ReportService
{
    public const int HistoryLimit = 10;
    private readonly Queue<RetentionReport> reports = new();
    private readonly object gate = new();

    // Only finished reports are published, so HTTP serialization never races the job.
    public void AddReport(RetentionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (gate)
        {
            reports.Enqueue(report);
            while (reports.Count > HistoryLimit)
                reports.Dequeue();
        }
    }

    public IReadOnlyList<RetentionReport> GetReports()
    {
        lock (gate)
            return reports.ToArray();
    }
}
