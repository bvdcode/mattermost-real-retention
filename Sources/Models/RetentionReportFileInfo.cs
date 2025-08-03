namespace Mattermost.RealRetention.Models
{
    public class RetentionReportFileInfo
    {
        public string RelativePath { get; set; } = string.Empty;
        public long FileLength { get; set; }
        public bool IsDeleted { get; set; }
        public string Result { get; set; } = string.Empty;
    }
}