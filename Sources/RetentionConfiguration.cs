using Npgsql;

namespace Mattermost.RealRetention;

public static class RetentionConfiguration
{
    public static void Bind(IConfiguration configuration, RetentionOptions options)
    {
        // Keep legacy settings working; explicit UPPER_SNAKE_CASE settings take precedence.
        configuration.Bind(options);
        options.DryRun = configuration.GetValue<bool?>("DRY_RUN") ?? options.DryRun;
        options.WebhookUrl = configuration["WEBHOOK_URL"] ?? options.WebhookUrl;
        options.DataDirectory = configuration["DATA_DIRECTORY"] ?? options.DataDirectory;
        options.BatchSize = configuration.GetValue<int?>("BATCH_SIZE") ?? options.BatchSize;
        options.DelayBetweenFilesInMs = configuration.GetValue<int?>("DELAY_BETWEEN_FILES_MS") ?? options.DelayBetweenFilesInMs;
    }

    public static string GetConnectionString(IConfiguration configuration) => new NpgsqlConnectionStringBuilder
    {
        Host = configuration["DATABASE_HOST"] ?? configuration["PostgresHost"] ?? configuration["DatabaseSettings:Host"] ?? "postgres-server",
        Port = configuration.GetValue<int?>("DATABASE_PORT") ?? configuration.GetValue<int?>("PostgresPort") ?? configuration.GetValue<int?>("DatabaseSettings:Port") ?? 5432,
        Username = configuration["DATABASE_USER"] ?? configuration["PostgresUser"] ?? configuration["DatabaseSettings:Username"] ?? "mattermost_server",
        Password = configuration["DATABASE_PASSWORD"] ?? configuration["PostgresPassword"] ?? configuration["DatabaseSettings:Password"],
        Database = configuration["DATABASE_NAME"] ?? configuration["PostgresDatabase"] ?? configuration["DatabaseSettings:Database"] ?? "mattermost"
    }.ConnectionString;
}
