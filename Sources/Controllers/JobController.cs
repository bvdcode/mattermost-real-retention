using Quartz;
using Microsoft.AspNetCore.Mvc;
using Mattermost.RealRetention.Jobs;
using Mattermost.RealRetention.Models;
using Mattermost.RealRetention.Services;

namespace Mattermost.RealRetention.Controllers
{
    public class JobController(ISchedulerFactory _scheduler, ILogger<JobController> _logger, ReportService _reports) : ControllerBase
    {
        [HttpGet("/status")]
        public IEnumerable<RetentionReport> GetReports()
        {
            var reports = _reports.GetReports();
            _logger.LogInformation("Returning {count} retention reports.", reports.Count());
            return reports;
        }

        [HttpGet("/trigger")]
        [HttpPost("/trigger")]
        public async Task<IActionResult> TriggerJob(CancellationToken cancellationToken)
        {
            var scheduler = await _scheduler.GetScheduler(cancellationToken);
            await scheduler.TriggerJob(RetentionJob.Key, cancellationToken: cancellationToken);
            _logger.LogInformation("Job '{jobName}' has been triggered successfully.", nameof(RetentionJob));
            return Ok($"Job '{nameof(RetentionJob)}' has been triggered successfully.");
        }
    }
}
