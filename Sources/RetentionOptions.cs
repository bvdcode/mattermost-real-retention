using System.ComponentModel.DataAnnotations;

namespace Mattermost.RealRetention;

public sealed class RetentionOptions
{
    public bool DryRun { get; set; } = true;

    [Required]
    public string DataDirectory { get; set; } = "/mattermost/data";

    [Range(1, 5000)]
    public int BatchSize { get; set; } = 200;

    [Range(0, 60000)]
    public int DelayBetweenFilesInMs { get; set; }
}
