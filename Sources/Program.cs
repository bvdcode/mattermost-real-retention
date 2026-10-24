using Mattermost.RealRetention.Database;
using Mattermost.RealRetention.Jobs;
using Mattermost.RealRetention.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quartz;

namespace Mattermost.RealRetention;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var configuration = builder.Configuration;
        builder.Services.AddOptions<RetentionOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Preserve the environment variables used by existing Docker deployments.
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["PostgresHost"] ?? configuration["DatabaseSettings:Host"] ?? "postgres-server",
            Port = configuration.GetValue("PostgresPort", configuration.GetValue("DatabaseSettings:Port", 5432)),
            Username = configuration["PostgresUser"] ?? configuration["DatabaseSettings:Username"] ?? "mattermost_server",
            Password = configuration["PostgresPassword"] ?? configuration["DatabaseSettings:Password"],
            Database = configuration["PostgresDatabase"] ?? configuration["DatabaseSettings:Database"] ?? "mattermost"
        };
        builder.Services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connection.ConnectionString, postgres => postgres.CommandTimeout(60))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        builder.Services.AddScoped<RetentionService>();
        builder.Services.AddSingleton<ReportService>();
        builder.Services.AddControllers();
        builder.Services.AddQuartz(quartz =>
        {
            quartz.AddJob<RetentionJob>(job => job.WithIdentity(RetentionJob.Key));
            quartz.AddTrigger(trigger => trigger
                .WithIdentity("DailyRetention")
                .ForJob(RetentionJob.Key)
                .StartNow()
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromHours(24))
                    .RepeatForever()
                    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount)));
        });
        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
