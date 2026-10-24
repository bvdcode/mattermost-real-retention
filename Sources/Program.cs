using Mattermost.RealRetention.Database;
using Mattermost.RealRetention.Jobs;
using Mattermost.RealRetention.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Mattermost.RealRetention;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var configuration = builder.Configuration;
        builder.Services.AddOptions<RetentionOptions>()
            .Configure(options => RetentionConfiguration.Bind(configuration, options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(RetentionConfiguration.GetConnectionString(configuration), postgres => postgres.CommandTimeout(60))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        builder.Services.AddScoped<RetentionService>();
        builder.Services.AddSingleton<ReportService>();
        builder.Services.AddHttpClient<IRetentionNotifier, MattermostNotifier>(client => client.Timeout = TimeSpan.FromSeconds(10))
            // The URL contains the webhook secret; HttpClient's default request logging would expose it.
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
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
