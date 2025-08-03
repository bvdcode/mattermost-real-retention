using System.ComponentModel.DataAnnotations.Schema;

namespace Mattermost.RealRetention.Database.Models
{
    [Table("fileinfo")]
    public class MattermostFileInfo
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("deleteat")]
        public long DeletedAt { get; set; }

        [Column("path")]
        public string Path { get; set; } = string.Empty;

        [Column("thumbnailpath")]
        public string ThumbnailPath { get; set; } = string.Empty;

        [Column("previewpath")]
        public string PreviewPath { get; set; } = string.Empty;

        [Column("postid")]
        public string PostId { get; set; } = string.Empty;
    }
}
