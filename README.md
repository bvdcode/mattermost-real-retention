# Mattermost Real Retention

[![CI](https://github.com/bvdcode/mattermost-real-retention/actions/workflows/docker-image.yml/badge.svg)](https://github.com/bvdcode/mattermost-real-retention/actions/workflows/docker-image.yml)
[![Release](https://img.shields.io/github/v/release/bvdcode/mattermost-real-retention?sort=semver)](https://github.com/bvdcode/mattermost-real-retention/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/bvdcode/mattermost-real-retention)](https://hub.docker.com/r/bvdcode/mattermost-real-retention)
[![Image Size](https://img.shields.io/docker/image-size/bvdcode/mattermost-real-retention/latest)](https://hub.docker.com/r/bvdcode/mattermost-real-retention/tags)
[![License](https://img.shields.io/github/license/bvdcode/mattermost-real-retention)](LICENSE)
[![CodeFactor](https://www.codefactor.io/repository/github/bvdcode/mattermost-real-retention/badge)](https://www.codefactor.io/repository/github/bvdcode/mattermost-real-retention)

**Safely deletes orphaned files in Mattermost Community/Team Edition. No Enterprise. No nonsense.**

- 🧹 Finds files whose posts no longer exist and removes them (attachments, thumbs, previews).
- 🔒 Safe by default: **dry-run** first, detailed logs.
- 🐳 Docker-first. Works with PostgreSQL + local filestore (`/mattermost/data`).
- ⚡ Proven scale: handled **~1M posts / 50k files** in ~**5 min**, ~**150 MB RAM**, ~**50% of 1 core**.

> Free your storage without paying for “retention” features.

<img width="718" height="459" alt="image" src="https://github.com/user-attachments/assets/53142610-f641-4305-8e29-872fd3d9156f" />

## ✨ Features

- 🔄 **Automatic cleanup**: Daily orphaned file cleanup task execution
- 🛡️ **Safety first**: Dry run mode enabled by default for testing
- 📊 **Detailed logging**: Comprehensive information about the cleanup process
- 🐳 **Docker Ready**: Ready-to-use Docker image for quick deployment
- ⚡ **High performance**: Built on .NET 9.0 with database preloading and smart caching
- 🔒 **Safe database operations**: Removes both file records from database and physical files from filesystem
- 🔌 **REST API**: HTTP endpoints for manual job triggering and status monitoring

## 🏗️ Architecture

The project is built on a modern technology stack:

- **.NET 9.0** - Main platform
- **Entity Framework Core** - ORM for PostgreSQL database operations
- **Quartz.NET** - Task scheduler for automatic execution
- **ASP.NET Core** - Web API host
- **PostgreSQL** - Mattermost database

> Please note: The `DryRun` mode is enabled by default, meaning that files will not be deleted but only logged. Change this setting to `false` in production after testing. Author is not responsible for data loss. If you have any errors or questions, please open an issue.

### System Components

```
Sources/
├── Program.cs                    # Application entry point
├── Controllers/
│   └── JobController.cs         # REST API endpoints
├── Services/
│   └── ReportService.cs         # Retention reports management
├── Models/
│   ├── RetentionReport.cs       # Report data model
│   └── RetentionReportFileInfo.cs # File processing details
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
    # Optional: Expose API endpoints for manual control and monitoring
    # Comment out the ports section if you don't need external API access
    ports:
      - "8080:8080"  # API endpoints (optional)
    environment:
      - PostgresHost=postgres
      - PostgresPort=5432
      - PostgresUser=mattermost
      - PostgresPassword=changeme
      - PostgresDatabase=mattermost
      - DryRun=true # Set to false for actual deletion
      - DelayBetweenFilesInMs=0 # Optional: delay between file processing
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
  -p 8080:8080 \  # Optional: Only if you need API access
  -e PostgresHost=your_postgres_host \
  -e PostgresPort=5432 \
  -e PostgresUser=mattermost \
  -e PostgresPassword=your_password \
  -e PostgresDatabase=mattermost \
  -e DryRun=true \
  -v /path/to/mattermost/data:/mattermost/data:rw \
  bvdcode/mattermost-real-retention:latest
```

> **Note**: Remove the `-p 8080:8080` line if you don't need external access to the API endpoints. The service will work perfectly for automatic cleanup without exposing any ports.

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
| `DelayBetweenFilesInMs` | Delay between file processing (ms) | `0`            | ❌       |

### Database Connection Setup

The service uses the same PostgreSQL connection settings as your Mattermost server. Ensure that:

1. The user has read permissions on `posts` and `fileinfo` tables
2. The user has delete permissions on `fileinfo` table records (only when `DryRun=false`)
3. The service can connect to the Mattermost database

## 🔌 API Endpoints

The service provides REST API endpoints for monitoring and manual control. **Note**: API access is optional - the service works automatically without exposing any ports.

### When to expose API ports:

- ✅ **Manual job triggering**: When you need to run cleanup on-demand
- ✅ **Monitoring integration**: For external monitoring systems or dashboards  
- ✅ **Testing and debugging**: During initial setup and troubleshooting
- ✅ **Automation scripts**: If you have custom scripts that need job status

### When NOT to expose API ports:

- ❌ **Production environments**: If you only need automatic daily cleanup
- ❌ **Security-sensitive deployments**: To minimize attack surface
- ❌ **Simple setups**: When "set and forget" automatic operation is sufficient

### GET /status

Returns detailed reports of all retention job executions.

**Response:**
```json
[
  {
    "dryRun": true,
    "createdAt": "2025-01-15T10:30:00Z",
    "directory": "/mattermost/data/",
    "totalFilesCount": 1523,
    "foldersCount": 45,
    "processedFiles": [
      {
        "relativePath": "20241201/abc123/image.jpg",
        "length": 245760,
        "deleted": true,
        "result": "File not found in database - deleted from filesystem"
      }
    ]
  }
]
```

**Usage:**
```bash
# Only works if ports are exposed
curl http://localhost:8080/status
```

### POST /trigger

Manually triggers the retention cleanup job.

**Response:**
```json
"Job 'RetentionJob' has been triggered successfully."
```

**Usage:**
```bash
# Only works if ports are exposed
curl -X POST http://localhost:8080/trigger
```

> **Note**: The trigger endpoint is useful for testing and manual cleanup runs. The job will still respect the `DryRun` setting.

## 🔧 How It Works

### Algorithm

1. **File scanning**: Every 24 hours the service scans the `/mattermost/data/` directory
2. **Date-based file search**: Only processes directories in `YYYYMMDD` format
3. **Performance optimization**: 
   - Preloads up to 1M active posts into memory for fast lookup
   - Bulk loads file records for efficient database access
   - Uses AsNoTracking for read-only operations to reduce memory overhead
4. **Database verification**: For each file, checks:
   - Does a record exist in the `fileinfo` table
   - Is the file linked to an active post (not deleted)
   - Is the file itself marked as deleted
5. **Safe deletion**: Orphaned files are removed from both filesystem and database. The service deletes:
   - Physical files from the filesystem (`/mattermost/data/`)
   - Corresponding records from the `fileinfo` table in the database

### File Types for Deletion

The service deletes files in the following cases:

- ✅ File not found in `fileinfo` table
- ✅ File linked to a deleted post (`posts.deleteat > 0`)
- ✅ File marked as deleted (`fileinfo.deleteat > 0`)

### Safety

- 🔒 Never deletes files linked to active posts
- 📝 Detailed logging of all operations with sensitive data sanitization
- 🧪 Dry run mode for testing
- ⏱️ Configurable delay between file checks (default: 0ms for maximum speed)
- 🗃️ Cleans both filesystem and database records for consistency
- 🚀 Memory-efficient processing with database preloading and bulk operations

## 📊 Monitoring and Logging

### API Monitoring

Use the `/status` endpoint to programmatically monitor retention job executions:

- **Job history**: View all completed retention jobs with detailed statistics
- **File details**: See exactly which files were processed and their outcomes  
- **Performance metrics**: Track total files processed, execution time, and cleanup efficiency
- **Dry run validation**: Review what would be deleted before setting `DryRun=false`

### Log Levels

- **Information**: General process information
- **Warning**: Found orphaned files
- **Debug**: Detailed information about each file

### Log Examples

```
[Information] Starting retention job with delay 0 ms and dry run mode True.
[Information] Found 1523 files in 45 date directories in /mattermost/data/.
[Information] Preloading database posts for performance optimization...
[Information] Preloaded 987543 active posts from the database.
[Information] Preloading database files for performance optimization...
[Warning] File 20241201/abc...123/image.jpg not found in the database - deleting from filesystem.
[Warning] File 20241201/def...456/document.pdf is marked deleted - deleting file and database record.
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
- **RAM**: 512MB (tested with 150-200MB usage on 1M posts + 50K files)
- **Disk**: Minimum for image storage (~200MB)
- **Access**: Read access to Mattermost data directory
- **Network**: Connection to PostgreSQL server

### Performance Characteristics

**Tested on production scale:**
- **Database size**: ~1 million posts, ~50,000 fileinfo records
- **Memory usage**: 150-200MB RAM during execution
- **Processing speed**: Optimized with database preloading and minimal delays
- **Efficiency**: Bulk operations and smart caching for large datasets

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

- Create an [Issue](https://github.com/bvdcode/mattermost-real-retention/issues)
- Check existing Issues
- Review service logs for diagnostics
