using Mattermost.Maintenance.Database;
using EasyExtensions.Quartz.Extensions;
using EasyExtensions.EntityFrameworkCore.Npgsql.Extensions;

namespace Mattermost.Maintenance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddPostgresDbContext<AppDbContext>(builder.Configuration);
            builder.Services.AddQuartzJobs();
            
            var app = builder.Build();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
