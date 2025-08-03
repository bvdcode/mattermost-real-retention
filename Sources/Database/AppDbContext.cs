using Microsoft.EntityFrameworkCore;
using Mattermost.RealRetention.Database.Models;

namespace Mattermost.RealRetention.Database
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<MattermostPost> Posts { get; set; } = null!;
        public DbSet<MattermostFileInfo> Files { get; set; } = null!;
    }
}
