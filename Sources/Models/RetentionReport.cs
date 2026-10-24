namespace Mattermost.RealRetention.Models;

public sealed class RetentionReport
{
    public const int DetailLimit = 100;
    public int Delay { get; set; }
    public bool DryRun { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public string? Error { get; set; }
    public string Directory { get; set; } = string.Empty;
    public long CandidateRecordsCount { get; set; }
    public long DeletedRecordsCount { get; set; }
    public long SkippedRecordsCount { get; set; }
    public long FailedRecordsCount { get; set; }
    public long ProcessedFilesCount { get; private set; }
    public long DeletedFilesCount { get; private set; }
    public long WouldDeleteFilesCount { get; private set; }
    public long MissingFilesCount { get; private set; }
    public long DeletedBytes { get; private set; }
    public long WouldDeleteBytes { get; private set; }
    public bool DetailsTruncated => ProcessedFilesCount > ProcessedFiles.Count;
    public List<RetentionReportFileInfo> ProcessedFiles { get; } = [];

    public void OnFileProcessed(string path, long length, bool exists, bool dryRun)
    {
        ProcessedFilesCount++;
        if (!exists)
            MissingFilesCount++;
        else if (dryRun)
        {
            WouldDeleteFilesCount++;
            WouldDeleteBytes += length;
        }
        else
        {
            DeletedFilesCount++;
            DeletedBytes += length;
        }

        if (ProcessedFiles.Count < DetailLimit)
            ProcessedFiles.Add(new RetentionReportFileInfo
            {
                RelativePath = path,
                FileLength = length,
                IsDeleted = exists && !dryRun,
                Result = !exists ? "Already missing" : dryRun ? "Would delete" : "Deleted"
            });
    }
}
