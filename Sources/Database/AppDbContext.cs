using Microsoft.EntityFrameworkCore;
using Mattermost.Maintenance.Database.Models;

namespace Mattermost.Maintenance.Database
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<MattermostPost> Posts { get; set; } = null!;
        public DbSet<MattermostFileInfo> Files { get; set; } = null!;
    }
}
