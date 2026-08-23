# Backup Gateway

A centralized gateway for coordinating multi-source, multi-target backup operations, with a focus on orchestrating the lifecycle of target nodes (Wake-on-LAN and remote shutdown) while following best practices for security, observability, and maintainability.

The implemented MVP architecture starts with the [`docs/architecture` overview](docs/architecture/README.md). Product scope, user stories, and the implementation plan live under [`docs/pm`](docs/pm/README.md).

## Background

We are currently using [BorgBackup](https://borgbackup.readthedocs.io/en/stable/) for deduplicated remote backups with the [Cyborg Workflow Engine](https://github.com/frederik-hoeft/cyborg) as the orchestrator. Currently, backups are initiated by a cron job on the source server, which invokes the cyborg workflow to:

1. Wake up the target nodes via Wake-on-LAN.
2. Run borg for each backup job.
3. Remotely shut down the target nodes after the backup is complete.

## Problem Statement

The current setup works well for a single-source, multi-target backup scenario. However, we want to extend this setup to support:
- Multi-source, multi-target backups.
- Offsite targets, which may not be directly reachable via Wake-on-LAN from the source servers.
- Offsite sources, which may not have the required network access to initiate Wake-on-LAN on the target nodes.

The current solution does not scale well due to:
- concurrency issues, especially with Wake-on-LAN and remote shutdowns.
- maintainability and firewall configuration complexities, especially for routed Wake-on-LAN via static ARP entries and remote shutdowns via SSH.
- security concerns for exposing remote shutdown (SSH) to the wider internal network / VPNs.

## Proposed Solution: Backup Gateway

To address these issues, we propose implementing a "Backup Gateway" that acts as an authoritative coordinator for backup operations. The high-level interaction flow would be as follows:

1. Source servers call the Backup Gateway API to request the initiation of a backup job for a specific target node.
2. The Backup Gateway performs the necessary Wake-on-LAN operations to wake up the target node, performs health checks, and ensures that it is ready for the backup job.
3. Once the backup job is complete, the source server calls the Backup Gateway API again to request the termination of the backup job, which triggers the Backup Gateway to perform remote shutdown operations on the target node, if there are no other concurrent backup jobs using the same target node.

## Requirements

### Functional Requirements

- **Lifecycle API**: expose a secure, authenticated API for initiating and terminating backup jobs, which can be called by source servers or other orchestrators.
- **State Management**: maintain the state of target nodes, performing Wake-on-LAN and remote shutdown operations as needed to minimize idle uptime, reduce attack surface, and extend node hardware lifespan.
- **Synchronization**: handle concurrent backup initiations and terminations, ensuring that nodes are not shutdown while concurrently being used for another backup job.
- **Configuration Management**: use validated ASP.NET Core configuration for target network addresses, Wake-on-LAN settings, readiness checks, and fixed authenticated shutdown settings.

### Non-Functional Requirements

- **Audit Logging**: log all backup operations, including initiations, terminations, and node state changes for auditing and troubleshooting purposes.
- **Metrics and Monitoring**: expose Prometheus metrics for gateway health, leases, target lifecycle state, and startup/shutdown outcomes.
- **Containerization**: deploy the initial single-instance gateway and its PostgreSQL store with Docker. Horizontal gateway replicas require distributed coordination and are out of scope for the initial release.

### Out of Scope

In its initial implementation, **the Backup Gateway will explicitly not handle**:

- **Proxying** the actual backup data transfer: the source servers will continue to directly connect to the target nodes for data transfer, and the Backup Gateway will only coordinate the lifecycle of the target nodes.
- **Backup job scheduling**: the Backup Gateway will not be responsible for scheduling backup jobs; it will only respond to API calls to initiate or terminate backup jobs. Scheduling continues to be a source server responsibility, which can be implemented via cron jobs or other orchestrators.
- **Encryption key management**: the Backup Gateway will not manage encryption keys for the backup data; this responsibility remains with the source servers or other dedicated key management systems.

### Technical Implementation Requirements

- ASP.NET Core Web API on .NET 10 / C# 14.
- PostgreSQL via Npgsql/EF Core, including ASP.NET Core Identity for authentication and authorization state.
- WKG ASP.NET Core transaction management and WKG Entity Framework Core model discovery/mapping conventions, following `wkg-framework-demo`.
- Modern `.slnx` solution format and the project coding conventions from `frederik-hoeft/csharp-syle-guide`.

## Development

The .NET solution lives under `Source/` and uses the same `Source/`-scoped project layout as the reference repositories. The current foundation contains the ASP.NET Core application plus separate unit and integration test projects.

Build and test from the repository root:

```bash
dotnet build Source/BackupGateway.slnx
dotnet test Source/BackupGateway.slnx
```

Run the API directly:

```bash
dotnet run --project Source/BackupGateway.Web
```

The liveness endpoint is available at `/health/live`, readiness at `/health/ready`, and Prometheus metrics at `/metrics`. Start with the [architecture overview](docs/architecture/README.md) for the system model and links to focused subsystem documentation. Product requirements and implementation tasks live under [`docs/pm`](docs/pm/README.md). The checked-in [API v1 contract](docs/api/README.md) and [production deployment guide](docs/deployment.md) define the external integration and operational boundaries.

### Integration tests

Persistence integration tests require a dedicated PostgreSQL database supplied through `BACKUP_GATEWAY_TEST_DATABASE`. The test suite recreates this database, so the connection must never point at a development or production database.

### Docker Compose

The development Compose stack starts the gateway and PostgreSQL 18. Database and authentication secrets are deliberately not stored in the repository. Create local secret files and an untracked `.env` before the first start:

```bash
mkdir -p secrets
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out secrets/jwt-signing-key.pem
openssl rand -base64 32 > secrets/bootstrap-admin-credential
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -base64 32)" > .env
chmod 600 secrets/* .env
# The image's non-root app user must be able to read bind-mounted files. Prefer an ACL
# over widening the host-side mode when your filesystem supports it.
setfacl -m u:1654:r secrets/jwt-signing-key.pem secrets/bootstrap-admin-credential
docker compose up --build
```

The official .NET Linux image currently uses UID `1654` for its `app` user. If your deployment uses a different image/user mapping, inspect the built image and grant that UID read access instead. `setfacl` is only a development example; production secret provisioning should grant the container identity the minimum required read permission without making private keys group/world-readable.

The first start creates the `admin` Identity user from `secrets/bootstrap-admin-credential`. The bootstrap secret cannot replace an existing administrator once Identity contains users. The JWT private key remains mounted read-only because it is required for token issuance and validation.

The gateway binds to `127.0.0.1:8080` by default. Set `BACKUP_GATEWAY_PORT` to change the host port. PostgreSQL is only reachable on the Compose network; use `docker compose exec postgres psql` for local administrative access. The Compose stack passes the PostgreSQL settings to the gateway through the standard `ConnectionStrings__DatabaseConnection` environment variable. When running the gateway directly, provide the same connection-string key and the `Auth:Jwt` / initial `Auth:BootstrapAdministrator` settings through user secrets or the environment. See the authentication architecture document for the complete contract.

### Target lifecycle configuration

Each target is configured through the standard `Targets:<target-id>` configuration hierarchy. The definition includes its host, Wake-on-LAN MAC/destination, TCP readiness probe, and fixed SSH shutdown contract. The SSH private key path must point at a file mounted into the gateway container, and `Shutdown:HostKeyFingerprint` must contain the pinned OpenSSH `SHA256:` fingerprint for the target. The gateway rejects unsafe or incomplete target definitions during startup.

The runtime image contains the OpenSSH client used for host-key scanning and shutdown. The gateway itself still runs as the non-root `app` user; mounted SSH private keys therefore need to be readable by that user without being writable by the container.

Lifecycle reconciliation runs immediately on service startup and periodically thereafter. `Lifecycle:ReconciliationInterval` defaults to one minute and accepts values between ten seconds and one hour.

### Client-side aborts

Client liveness is tracked through lease heartbeats, but stale leases are never released automatically. Because backup traffic bypasses the gateway, a missing heartbeat cannot prove that a backup has stopped. A stale lease therefore continues to prevent shutdown until the client releases it or an administrator explicitly force-releases it.

### Future Considerations

- **Additional configuration providers**: add YAML-specific configuration support if it provides operational value beyond the standard JSON/environment-variable configuration pipeline.

- **Repository key backup**: in the future, the Backup Gateway could also be extended to securely store encrypted borg repository keys and repository metadata.
- **Target node discovery**: the Backup Gateway could act as a central registry for target nodes, allowing source servers to query available targets and their capabilities (e.g., available disk space, supported backup types, etc.) before initiating backup jobs. This would, however, either the source servers to reuse the same repository passphrase for all target nodes, or introduce key management challlenges if different passphrases are used for different target nodes.
- **Time-based Access Control**: the Backup Gateway could implement time-based access control policies, allowing specific to only initiate backup jobs during certain time windows (e.g., outside of business hours) to minimize impact on network performance and target node availability. This would require the Backup Gateway to maintain a schedule of allowed backup times for each source server or target node, and enforce these policies when processing API requests.