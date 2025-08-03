using EasyExtensions.Quartz.Extensions;
using Mattermost.RealRetention.Database;
using Mattermost.RealRetention.Services;
using EasyExtensions.EntityFrameworkCore.Npgsql.Extensions;

namespace Mattermost.RealRetention
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
