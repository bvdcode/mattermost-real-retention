using System.Globalization;
using EasyExtensions.Quartz.Attributes;
using Mattermost.Maintenance.Database;
using Quartz;

namespace Mattermost.Maintenance.Jobs
{
    [JobTrigger(days: 7)]
    public class RetentionJob(AppDbContext _dbContext, ILogger<RetentionJob> _logger) : IJob
    {
        private const string folder = "/storage/mattermost/data/";

        public async Task Execute(IJobExecutionContext context)
        {
            DirectoryInfo di = new(folder);
            if (!di.Exists)
            {
                throw new DirectoryNotFoundException($"The directory {folder} does not exist.");
            }
            int counter = 0;

            var dateDirectories = di.GetDirectories()
                .Where(d =>
                    d.Name.Length == 8
                    && DateTime.TryParseExact(d.Name, "yyyyMMdd", null, DateTimeStyles.None, out _)
                )
                .ToList();

            var files = dateDirectories
                .SelectMany(d => d.GetFiles("*", SearchOption.AllDirectories))
                .ToArray();

            _logger.LogInformation(
                "Found {filesLength} files in {directoryCount} date directories in {folder}.",
                files.Length,
                dateDirectories.Count,
                folder
            );

            foreach (var file in files)
            {
                await Task.Delay(250);
                string relativePath = file.FullName.Replace(folder, string.Empty);
                _logger.LogDebug("Processing file {fileName}.", relativePath);
                var fileInfo = _dbContext.Files.FirstOrDefault(f =>
                    f.Path == relativePath
                    || f.ThumbnailPath == relativePath
                    || f.PreviewPath == relativePath
                );
                if (fileInfo == null)
                {
                    _logger.LogWarning(
                        "File {fileName} not found in the database - deleting from filesystem.",
                        relativePath
                    );
                    file.Delete();
                    counter++;
                    continue;
                }
                var postExists = _dbContext.Posts.Any(p =>
                    p.Id == fileInfo.PostId && p.DeletedAt == 0
                );
                if (postExists && fileInfo.DeletedAt == 0)
                {
                    _logger.LogInformation(
                        "File {fileName} with ID {fileId} is associated with an active post, skipping deletion.",
                        relativePath,
                        fileInfo.Id
                    );
                    continue;
                }
                else
                {
                    _logger.LogWarning(
                        fileInfo.DeletedAt > 0
                            ? "File {fileName} with ID {fileId} is marked deleted - deleting."
                            : "File {fileName} with ID {fileId} is associated with a deleted post - deleting.",
                        relativePath,
                        fileInfo.Id
                    );
                    _dbContext.Files.Remove(fileInfo);
                    await _dbContext.SaveChangesAsync();
                    file.Delete();
                    counter++;
                }
            }

            _logger.LogInformation(
                "Retention job completed. {counter} files deleted, {remaining} files total.",
                counter,
                files.Length
            );
        }
    }
}
