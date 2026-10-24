# Mattermost Real Retention

[![CI](https://github.com/bvdcode/mattermost-real-retention/actions/workflows/docker-image.yml/badge.svg)](https://github.com/bvdcode/mattermost-real-retention/actions/workflows/docker-image.yml)
[![Release](https://img.shields.io/github/v/release/bvdcode/mattermost-real-retention?sort=semver)](https://github.com/bvdcode/mattermost-real-retention/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/bvdcode/mattermost-real-retention)](https://hub.docker.com/r/bvdcode/mattermost-real-retention)
[![License](https://img.shields.io/github/license/bvdcode/mattermost-real-retention)](LICENSE)

Reclaim local disk space used by deleted Mattermost attachments. Runs independently of Mattermost with access to its PostgreSQL database and local data directory. No Enterprise license is required.

The service runs at startup and every 24 hours. **Dry-run is enabled by default.** It does not delete messages or apply an age-based retention policy.

## What gets removed

An attachment is eligible only when PostgreSQL confirms either:

- Its `fileinfo.deleteat` is greater than zero.
- Its linked post exists and `posts.deleteat` is greater than zero.

For each eligible attachment, the service processes its original file, thumbnail and preview. It deletes the `fileinfo` row only after all those paths have been handled successfully. Missing files count as already cleaned. Post rows are never changed.

Active attachments, uploads without an attached post, records referencing a missing post, and disk files with no database record are left alone unless the file record itself is explicitly marked deleted. A path referenced by another attachment is also left alone and logged for inspection.

**Version 2 changes the deletion rule:** version 1 also deleted files simply because their database record or post was missing. Version 2 requires explicit evidence of deletion. This avoids deleting uploads in progress and removes the old one-million-post cutoff.

## Docker Compose

Connect the container to the same Docker network as PostgreSQL and mount the **actual Mattermost local filestore**:

```yaml
services:
  mattermost-real-retention:
    image: bvdcode/mattermost-real-retention:latest
    restart: unless-stopped
    environment:
      PostgresHost: postgres
      PostgresPort: 5432
      PostgresUser: mattermost
      PostgresPassword: ${MATTERMOST_DB_PASSWORD}
      PostgresDatabase: mattermost
      DryRun: "true"
    volumes:
      - /path/to/mattermost/data:/mattermost/data:rw
    # Optional. No published port or reverse proxy is needed for scheduled cleanup.
    # ports:
    #   - "127.0.0.1:8080:8080"
```

```bash
docker compose up -d
docker compose logs -f mattermost-real-retention
```

Check the dry-run summary and ensure that the mounted directory belongs to this database. To enable deletion, change `DryRun` to `"false"` and recreate the container with `docker compose up -d`. Keep backups before enabling permanent deletion.

The database account needs `SELECT` on `posts` and `fileinfo`, `DELETE` on `fileinfo`, and the `UPDATE` privileges PostgreSQL requires for row locking. The service does not issue `UPDATE` statements, create tables, run migrations, or alter Mattermost's schema. The filesystem account needs permission to read files and remove entries from their parent directories.

Only PostgreSQL and a local filestore are supported. S3/object storage is not supported. An absent or empty data directory stops a destructive run; this helps catch missing volume mounts but cannot verify that a nonempty mount belongs to the selected database. Symbolic links inside attachment paths are rejected. Do not replace the mounted directory or change its links while a run is active.

## Configuration

Existing `Postgres*`, `DryRun` and `DelayBetweenFilesInMs` environment variables continue to work.

| Variable | Default | Purpose |
| --- | --- | --- |
| `PostgresHost` | `postgres-server` | PostgreSQL host |
| `PostgresPort` | `5432` | PostgreSQL port |
| `PostgresUser` | `mattermost_server` | PostgreSQL username |
| `PostgresPassword` | unset | PostgreSQL password |
| `PostgresDatabase` | `mattermost` | Mattermost database |
| `DryRun` | `true` | Report intended deletions without changing files or database rows |
| `DataDirectory` | `/mattermost/data` | Local filestore mount |
| `BatchSize` | `200` | Maximum candidate IDs loaded at once; range 1–5000 |
| `DelayBetweenFilesInMs` | `0` | Optional delay per attachment path; range 0–60000 |

`DatabaseSettings:Host`, `Port`, `Username`, `Password` and `Database` remain available in ASP.NET configuration as fallbacks for the corresponding `Postgres*` settings.

## Memory and failure handling

- Candidates are selected in PostgreSQL and read in bounded batches, using the last processed ID instead of offset pagination. The service never preloads all active posts or enumerates the entire filestore.
- Database reads do not populate EF's change tracker. Only one batch of IDs and the current attachment are needed in memory.
- A short transaction per attachment locks its current metadata and, when deletion depends on the post, the post's deletion state. The state is checked again before any files are removed.
- Original, thumbnail and preview paths are checked before deletion. Paths outside the configured directory, directory paths, and symbolic links are rejected.
- A filesystem error keeps the row for retry and does not prevent other attachments from being processed. A database error fails that run; the next scheduled or manual run retries remaining rows.
- Filesystem deletion and a PostgreSQL transaction cannot be atomic. If a process stops after deleting a file but before committing the row deletion, the next run tolerates the missing file and finishes cleanup.
- Quartz prevents overlapping executions of the job in one service instance. Database row locks also serialize access to the same attachment across instances. A single container is recommended.
- Reports retain the last 10 finished runs and at most 100 file details per run. Aggregate counters still include every processed file. Reports are lost on restart.

## Optional HTTP API

The container listens on port 8080. Scheduled cleanup does not require publishing that port. The API has no authentication; publish it only where manual access is intended.

- `GET /status` returns the last 10 **finished** reports, oldest first. While the first run is executing it returns `[]`; use container logs for progress.
- `POST /trigger` queues another execution. `GET /trigger` remains supported for compatibility. Requests respect `DryRun` and do not run concurrently within the same scheduler.

```bash
curl http://localhost:8080/status
curl -X POST http://localhost:8080/trigger
```

Reports include `status` (`Completed`, `CompletedWithErrors`, `Failed`, or `Cancelled`), timestamps, candidate/deleted/skipped/failed record counts, deleted/missing/would-delete file counts, byte counts, and a bounded `processedFiles` sample. In dry-run, actual deletion counters stay zero; use `wouldDeleteFilesCount` and `wouldDeleteBytes`. `detailsTruncated` indicates that the sample is incomplete. A failed run exposes the exception type in `error`; details are logged.

Version 2 removes `totalFilesCount` and `foldersCount`: the service no longer inventories the disk. `processedFilesCount` is an aggregate rather than the sample length.

## Development and tests

Requires .NET 10 SDK. The service uses EF Core 10, Npgsql and Quartz 4 directly.

```bash
dotnet build Sources/Mattermost.RealRetention.sln --configuration Release
dotnet run --project Sources/Mattermost.RealRetention.csproj
```

For local development the launch profile listens on `http://localhost:5277`. Override `DataDirectory` and the PostgreSQL settings before running. The job starts automatically, including in development.

Tests use a real, **isolated test PostgreSQL database** and temporary files. Each test creates its own schema; do not point tests at the Mattermost database. Run filesystem permission tests as a non-root Linux user.

```bash
docker run --rm -d --name retention-test-db \
  -e POSTGRES_PASSWORD=retention-tests \
  -e POSTGRES_DB=retention_tests \
  -p 127.0.0.1:55432:5432 postgres:18

export RETENTION_TEST_DATABASE='Host=localhost;Port=55432;Database=retention_tests;Username=postgres;Password=retention-tests'
dotnet test Sources/Mattermost.RealRetention.sln --configuration Release

docker stop retention-test-db
```

Coverage includes a database with over a million active posts, batched cleanup, active/unattached/missing-post preservation, dry-run, previews, repeated runs, shared paths, unsafe paths, permission failures, cancellation, concurrent execution, post restoration while waiting for a lock, recovery after a database failure, and bounded report storage.

Build the container with:

```bash
docker build -t mattermost-real-retention ./Sources
```

## CI and images

GitHub Actions builds and tests against PostgreSQL before publishing images. Pull requests run tests without publishing. Changes to source, tests, package configuration or the workflow trigger CI; publication is limited to `main` and manual runs on `main`.

GitHub releases and versioned Docker tags use `Major.Minor.Patch` (for example, `2.0.0`), without a `v` prefix or prerelease suffix. Images are published to Docker Hub (`bvdcode/mattermost-real-retention`) and GHCR (`ghcr.io/bvdcode/mattermost-real-retention`) with that version, `latest`, and `sha-<full commit SHA>`. Use a full commit tag when pinning an exact build.

## License

MIT. See [LICENSE](LICENSE).
