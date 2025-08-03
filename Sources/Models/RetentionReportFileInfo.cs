namespace Mattermost.Maintenance.Models
{
    public class RetentionReportFileInfo
    {
        public string RelativePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public bool Deleted { get; set; }
        public string Result { get; set; } = string.Empty;
    }
}