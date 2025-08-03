using System.ComponentModel.DataAnnotations.Schema;

namespace Mattermost.Maintenance.Database.Models
{
    [Table("posts")]
    public class MattermostPost
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("deleteat")]
        public long DeletedAt { get; set; }
    }
}
