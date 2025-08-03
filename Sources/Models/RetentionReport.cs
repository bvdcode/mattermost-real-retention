namespace Mattermost.RealRetention.Models
{
    public class RetentionReport
    {
        public int Delay { get; set; }
        public bool DryRun { get; set; }
        public int FoldersCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalFilesCount { get; set; }
        public int DeletedFilesCount => ProcessedFiles.Count(f => f.IsDeleted);
        public int ProcessedFilesCount => ProcessedFiles.Count;
        public string Directory { get; set; } = string.Empty;
        public List<RetentionReportFileInfo> ProcessedFiles { get; set; } = [];

        public void OnFileProcessed(string relativePath, long length, bool deleted, string result)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
            }
            ProcessedFiles.Add(new RetentionReportFileInfo
            {
                RelativePath = relativePath,
                FileLength = length,
                IsDeleted = deleted,
                Result = result
            });
        }
    }
}