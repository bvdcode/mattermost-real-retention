using Mattermost.RealRetention.Services;
using Quartz;

namespace Mattermost.RealRetention.Jobs;

[DisallowConcurrentExecution]
public sealed class RetentionJob(RetentionService retention) : IJob
{
    public static readonly JobKey Key = new(nameof(RetentionJob));

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await retention.RunAsync(cancellationToken);
    }
}
