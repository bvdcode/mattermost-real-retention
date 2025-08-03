# Mattermost Real File Retention

![Docker Pulls](https://img.shields.io/docker/pulls/bvdcode/mattermost-real-retention)
![Docker Tag](https://img.shields.io/docker/v/bvdcode/mattermost-real-retention)
![.NET](https://img.shields.io/badge/.NET-9.0-blue)

**Safe automatic cleanup of orphaned files in Mattermost Community/Team Edition without requiring an Enterprise license.**

The service automatically finds and removes files that are no longer linked to active posts in Mattermost, helping save disk space and maintain a clean file system.

> Please note: The `DryRun` mode is enabled by default, meaning that files will not be deleted but only logged. Change this setting to `false` in production after testing. Author is not responsible for data loss. If you have any errors or questions, please open an issue.

<img width="718" height="459" alt="image" src="https://github.com/user-attachments/assets/53142610-f641-4305-8e29-872fd3d9156f" />

## ✨ Features

- 🔄 **Automatic cleanup**: Daily orphaned file cleanup task execution
- 🛡️ **Safety first**: Dry run mode enabled by default for testing
- 📊 **Detailed logging**: Comprehensive information about the cleanup process
- 🐳 **Docker Ready**: Ready-to-use Docker image for quick deployment
- ⚡ **High performance**: Built on .NET 9.0 with optimization
- 🔒 **Safe database operations**: Only removes file records, no Mattermost data changes

## 🏗️ Architecture

The project is built on a modern technology stack:

- **.NET 9.0** - Main platform
- **Entity Framework Core** - ORM for PostgreSQL database operations
- **Quartz.NET** - Task scheduler for automatic execution
- **ASP.NET Core** - Web API host
- **PostgreSQL** - Mattermost database

### System Components

```
Sources/
├── Program.cs                    # Application entry point
├── Database/
│   ├── AppDbContext.cs          # Entity Framework context
│   └── Models/
│       ├── MattermostPost.cs    # Mattermost posts model
│       └── MattermostFileInfo.cs # File information model
└── Jobs/
    └── RetentionJob.cs          # Main file cleanup job
```

## 🚀 Quick Start

### Using Docker Compose (Recommended)

1. Create a `docker-compose.yml` file:

```yaml
services:
  mattermost-real-retention:
    image: bvdcode/mattermost-real-retention:latest
    restart: always
    environment:
      - PostgresHost=postgres
      - PostgresPort=5432
      - PostgresUser=mattermost
      - PostgresPassword=changeme
      - PostgresDatabase=mattermost
      - DryRun=true # Set to false for actual deletion
    volumes:
      - /path/to/mattermost/data:/mattermost/data:rw
    networks:
      - mattermost_network
```

2. Start the container:

```bash
docker-compose up -d
```

### Using Docker

```bash
docker run -d \
  --name mattermost-retention \
  --restart always \
  -e PostgresHost=your_postgres_host \
  -e PostgresPort=5432 \
  -e PostgresUser=mattermost \
  -e PostgresPassword=your_password \
  -e PostgresDatabase=mattermost \
  -e DryRun=true \
  -v /path/to/mattermost/data:/mattermost/data:rw \
  bvdcode/mattermost-real-retention:latest
```

## ⚙️ Configuration

### Environment Variables

| Variable           | Description                      | Default             | Required |
| ------------------ | -------------------------------- | ------------------- | -------- |
| `PostgresHost`     | PostgreSQL server host           | `postgres-server`   | ✅       |
| `PostgresPort`     | PostgreSQL port                  | `5432`              | ❌       |
| `PostgresUser`     | PostgreSQL username              | `mattermost_server` | ✅       |
| `PostgresPassword` | PostgreSQL password              | -                   | ✅       |
| `PostgresDatabase` | Database name                    | `mattermost`        | ✅       |
| `DryRun`           | Test mode (doesn't delete files) | `true`              | ❌       |

### Database Connection Setup

The service uses the same PostgreSQL connection settings as your Mattermost server. Ensure that:

1. The user has read permissions on `posts` and `fileinfo` tables
2. The user has delete permissions on `fileinfo` table records (only when `DryRun=false`)
3. The service can connect to the Mattermost database

## 🔧 How It Works

### Algorithm

1. **File scanning**: Every 24 hours the service scans the `/mattermost/data/` directory
2. **Date-based file search**: Only processes directories in `YYYYMMDD` format
3. **Database verification**: For each file, checks:
   - Does a record exist in the `fileinfo` table
   - Is the file linked to an active post (not deleted)
   - Is the file itself marked as deleted
4. **Safe deletion**: Orphaned files are removed from both filesystem and database

### File Types for Deletion

The service deletes files in the following cases:

- ✅ File not found in `fileinfo` table
- ✅ File linked to a deleted post (`posts.deleteat > 0`)
- ✅ File marked as deleted (`fileinfo.deleteat > 0`)

### Safety

- 🔒 Never deletes files linked to active posts
- 📝 Detailed logging of all operations
- 🧪 Dry run mode for testing
- ⏱️ 250ms delay between file checks to reduce load

## 📊 Monitoring and Logging

### Log Levels

- **Information**: General process information
- **Warning**: Found orphaned files
- **Debug**: Detailed information about each file

### Log Examples

```
[Information] Found 1523 files in 45 date directories in /mattermost/data/.
[Warning] File 20241201/abc123/image.jpg not found in the database - deleting from filesystem.
[Information] Dry run enabled, skipping actual deletion.
[Information] Retention job completed. 42 files deleted, 1523 files total.
```

## 🛠️ Development

### Requirements

- .NET 9.0 SDK
- PostgreSQL (for testing)
- Docker (optional)

### Building the Project

```bash
cd Sources
dotnet restore
dotnet build
```

### Running in Development Mode

```bash
cd Sources
dotnet run
```

### Building Docker Image

```bash
docker build -t mattermost-retention ./Sources
```

## 🔄 CI/CD

The project uses GitHub Actions for automatic building and publishing of Docker images:

- **Docker Hub**: `bvdcode/mattermost-real-retention`
- **GitHub Container Registry**: `ghcr.io/bvdcode/mattermost-real-retention`

Images are built automatically on every push to the `main` branch.

## 📋 System Requirements

### Minimum Requirements

- **CPU**: 1 core
- **RAM**: 1GB
- **Disk**: Minimum for image storage (~200MB)
- **Access**: Read access to Mattermost data directory
- **Network**: Connection to PostgreSQL server

### Deployment Recommendations

- Run the service on the same server where Mattermost files are located
- Use network storage if Mattermost runs in a cluster
- Set up log monitoring to track service operation
- Start with `DryRun=true` to assess cleanup volume

## ❗ Important Notes

1. **Dry run mode**: Enabled by default `DryRun=true` - files are not deleted, only logged
2. **Backups**: Always create a backup of your data before first run
3. **Testing**: Test the service in dry run mode before production use
4. **Permissions**: Ensure the container has read/write permissions to the data directory

## 🤝 Contributing

Contributions to the project are welcome:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Create a Pull Request

## 📄 License

This project is distributed under the MIT License. See the [LICENSE](LICENSE) file for details.

## 🆘 Support

If you have questions or issues:

- Create an [Issue](https://github.com/bvdcode/mattermost-real-file-retention/issues)
- Check existing Issues
- Review service logs for diagnostics
