using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Mattermost.RealRetention.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Uppercase_settings_take_precedence_over_legacy_names()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DRY_RUN"] = "false", ["DryRun"] = "true",
            ["DATA_DIRECTORY"] = "/new/data", ["DataDirectory"] = "/old/data",
            ["BATCH_SIZE"] = "37", ["BatchSize"] = "100",
            ["DELAY_BETWEEN_FILES_MS"] = "25", ["DelayBetweenFilesInMs"] = "0",
            ["WEBHOOK_URL"] = "https://example.test/hooks/new", ["WebhookUrl"] = "https://example.test/hooks/old",
            ["DATABASE_HOST"] = "new-host", ["PostgresHost"] = "old-host",
            ["DATABASE_PORT"] = "5433", ["PostgresPort"] = "5432",
            ["DATABASE_USER"] = "new-user", ["PostgresUser"] = "old-user",
            ["DATABASE_PASSWORD"] = "new-test-password", ["PostgresPassword"] = "old-test-password",
            ["DATABASE_NAME"] = "new-db", ["PostgresDatabase"] = "old-db"
        }).Build();
        var options = new RetentionOptions();
        RetentionConfiguration.Bind(configuration, options);
        Assert.False(options.DryRun);
        Assert.Equal("/new/data", options.DataDirectory);
        Assert.Equal(37, options.BatchSize);
        Assert.Equal(25, options.DelayBetweenFilesInMs);
        Assert.Equal("https://example.test/hooks/new", options.WebhookUrl);
        var connection = new NpgsqlConnectionStringBuilder(RetentionConfiguration.GetConnectionString(configuration));
        Assert.Equal("new-host", connection.Host);
        Assert.Equal(5433, connection.Port);
        Assert.Equal("new-user", connection.Username);
        Assert.Equal("new-test-password", connection.Password);
        Assert.Equal("new-db", connection.Database);
    }

    [Fact]
    public void Existing_deployment_names_continue_to_work()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DryRun"] = "false", ["DataDirectory"] = "/legacy/data", ["BatchSize"] = "51",
            ["DelayBetweenFilesInMs"] = "5", ["PostgresHost"] = "legacy-host", ["PostgresPort"] = "5434",
            ["PostgresUser"] = "legacy-user", ["PostgresPassword"] = "legacy-test-password", ["PostgresDatabase"] = "legacy-db"
        }).Build();
        var options = new RetentionOptions();
        RetentionConfiguration.Bind(configuration, options);
        Assert.False(options.DryRun);
        Assert.Equal("/legacy/data", options.DataDirectory);
        Assert.Equal(51, options.BatchSize);
        Assert.Equal(5, options.DelayBetweenFilesInMs);
        var connection = new NpgsqlConnectionStringBuilder(RetentionConfiguration.GetConnectionString(configuration));
        Assert.Equal("legacy-host", connection.Host);
        Assert.Equal(5434, connection.Port);
        Assert.Equal("legacy-user", connection.Username);
        Assert.Equal("legacy-test-password", connection.Password);
        Assert.Equal("legacy-db", connection.Database);
    }

    [Fact]
    public void Defaults_remain_dry_run_without_notifications()
    {
        var options = new RetentionOptions();
        RetentionConfiguration.Bind(new ConfigurationBuilder().Build(), options);
        Assert.True(options.DryRun);
        Assert.Null(options.WebhookUrl);
        Assert.Equal(200, options.BatchSize);
        Assert.Equal("/mattermost/data", options.DataDirectory);
    }

    [Fact]
    public void Empty_webhook_setting_disables_legacy_webhook()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBHOOK_URL"] = "", ["WebhookUrl"] = "https://example.test/hooks/old"
        }).Build();
        var options = new RetentionOptions();
        RetentionConfiguration.Bind(configuration, options);
        Assert.Equal("", options.WebhookUrl);
    }
}
