namespace Mattermost.Maintenance.Models
{
    public class RetentionReport
    {
        public bool DryRun { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Directory { get; set; } = string.Empty;
        public int TotalFilesCount { get; set; }
        public int FoldersCount { get; set; }
        public IList<RetentionReportFileInfo> ProcessedFiles { get; set; } = [];

        public void OnFileProcessed(string relativePath, long length, bool deleted, string result)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
            }
            ProcessedFiles.Add(new RetentionReportFileInfo
            {
                RelativePath = relativePath,
                Length = length,
                Deleted = deleted,
                Result = result
            });
        }
    }
}