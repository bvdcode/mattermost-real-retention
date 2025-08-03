using Quartz;
using Microsoft.AspNetCore.Mvc;
using Mattermost.Maintenance.Jobs;
using Mattermost.Maintenance.Models;
using Mattermost.Maintenance.Services;
using EasyExtensions.Quartz.Extensions;

namespace Mattermost.Maintenance.Controllers
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

        [HttpPost("/trigger")]
        public async Task<IActionResult> TriggerJob()
        {
            await _scheduler.TriggerJobAsync<RetentionJob>();
            _logger.LogInformation("Job '{jobName}' has been triggered successfully.", nameof(RetentionJob));
            return Ok($"Job '{nameof(RetentionJob)}' has been triggered successfully.");
        }
    }
}
