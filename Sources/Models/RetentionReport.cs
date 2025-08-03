namespace Mattermost.RealRetention.Models
{
    public class RetentionReport
    {
        public int Delay { get; set; }
        public bool DryRun { get; set; }
        public int FoldersCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalFilesCount { get; set; }
        public string Directory { get; set; } = string.Empty;
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