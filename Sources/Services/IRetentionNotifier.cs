using Mattermost.RealRetention.Models;

namespace Mattermost.RealRetention.Services;

public interface IRetentionNotifier
{
    Task NotifyAsync(RetentionReport report, CancellationToken cancellationToken = default);
}
