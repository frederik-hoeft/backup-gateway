# Backup Gateway (Design Draft)

A centralized gateway for coordinating multi-source, multi-target backup operations, with a focus on orchestrating the lifecycle of target nodes (Wake-on-LAN and remote shutdown) while following best practices for security, observability, and maintainability.

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
- **Configuration Management**: allow for flexible, JSON/YAML-based configuration of target nodes, including their network addresses, Wake-on-LAN settings, and shutdown commands.

### Non-Functional Requirements

- **Audit Logging**: log all backup operations, including initiations, terminations, and node state changes for auditing and troubleshooting purposes.
- **Metrics and Monitoring**: since target nodes are only available on demand, the Backup Gateway should act as a central proxy for monitoring node health (S.M.A.R.T. disk data, **free disk space**, startup/shutdown times, etc.). It should essentially act as a proxy gateway for prometheus metrics, which can be scraped by the existing observability stack. The gateway cache should be updated on demand (whenever a target node is woken up for a backup job).
- **Containerization**: the Backup Gateway should be designed to run in a containerized environment (e.g., Docker, Kubernetes) for ease of deployment and scalability.

### Out of Scope

In its initial implementation, **the Backup Gateway will explicitly not handle**:

- **Proxying** the actual backup data transfer: the source servers will continue to directly connect to the target nodes for data transfer, and the Backup Gateway will only coordinate the lifecycle of the target nodes.
- **Backup job scheduling**: the Backup Gateway will not be responsible for scheduling backup jobs; it will only respond to API calls to initiate or terminate backup jobs. Scheduling continues to be a source server responsibility, which can be implemented via cron jobs or other orchestrators.
- **Encryption key management**: the Backup Gateway will not manage encryption keys for the backup data; this responsibility remains with the source servers or other dedicated key management systems.

### Technical Implementation Requirements

- ASP.NET Core Web API on .NET 10+ for the API implementation, leveraging its built-in support for authentication, logging, and dependency injection.
- ASP.NET Core Identity for authentication and authorization, possibly using SQLite as a lightweight backing store for source server credentials and permissions.

### Open Questions

- **Client-side aborts**: how should the Backup Gateway handle cases where a source server initiates a backup job but fails to complete it (e.g., due to a crash or network failure)? Should the Backup Gateway implement a timeout mechanism to automatically terminate backup jobs that have been idle for too long, and if so, how would a secure timeout mechanism be implemented to prevent accidental termination of active backup jobs?

### Future Considerations

- **Repository key backup**: in the future, the Backup Gateway could also be extended to securely store encrypted borg repository keys and repository metadata.
- **Target node discovery**: the Backup Gateway could act as a central registry for target nodes, allowing source servers to query available targets and their capabilities (e.g., available disk space, supported backup types, etc.) before initiating backup jobs. This would, however, either the source servers to reuse the same repository passphrase for all target nodes, or introduce key management challlenges if different passphrases are used for different target nodes.
- **Time-based Access Control**: the Backup Gateway could implement time-based access control policies, allowing specific to only initiate backup jobs during certain time windows (e.g., outside of business hours) to minimize impact on network performance and target node availability. This would require the Backup Gateway to maintain a schedule of allowed backup times for each source server or target node, and enforce these policies when processing API requests.