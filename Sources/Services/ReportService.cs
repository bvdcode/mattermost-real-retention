using Mattermost.RealRetention.Models;

namespace Mattermost.RealRetention.Services
{
    public class ReportService
    {
        private readonly IList<RetentionReport> _reports = [];

        public void AddReport(RetentionReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report), "Report cannot be null.");
            }
            _reports.Add(report);
        }

        public IEnumerable<RetentionReport> GetReports()
        {
            return _reports.AsReadOnly();
        }
    }
}
