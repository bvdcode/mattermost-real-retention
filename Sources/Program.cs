using Mattermost.Maintenance.Database;
using Mattermost.Maintenance.Services;
using EasyExtensions.Quartz.Extensions;
using EasyExtensions.EntityFrameworkCore.Npgsql.Extensions;

namespace Mattermost.Maintenance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddQuartzJobs();
            builder.Services.AddControllers();
            builder.Services.AddSingleton<ReportService>();
            builder.Services.AddPostgresDbContext<AppDbContext>(builder.Configuration);

            var app = builder.Build();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
