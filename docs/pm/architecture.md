# Initial Architecture

## System boundary

Backup Gateway sits on the control path of a backup but not on its data path.

A backup source authenticates to the gateway and acquires a lease for a configured target. The gateway ensures that the target is online and ready. The source then connects directly to the target to perform the backup. When the source releases its lease, the gateway may power the target down once no other held leases remain.

This separation keeps backup throughput and repository credentials out of the gateway while centralizing the security-sensitive power-management operations that otherwise need to be exposed to every backup source.

## Core concepts

### Target definition

A target definition is immutable operational configuration from the perspective of a running application instance. It identifies a dedicated backup node and describes how the gateway can wake it, determine readiness, and shut it down.

Target definitions live in ASP.NET Core configuration rather than the runtime database. This keeps low-frequency infrastructure configuration versionable and keeps private key material under deployment-secret management. Database state refers to targets by stable configured identifiers.

### Backup lease

A backup lease is the authoritative durable statement that a client may currently use a target. Leases are identified by caller-generated UUIDs and owned by exactly one authenticated service identity and one target.

A lease is either held or released. Heartbeat freshness is diagnostic metadata and does not change whether a lease is held. This distinction is the central safety invariant: a client becoming unreachable is not evidence that its direct backup connection to the target has stopped.

### Target runtime state

The gateway maintains the latest observed lifecycle state for each configured target:

- `Unknown` - current reachability/power state has not been established;
- `Offline` - readiness checks confirm the target is unavailable after an expected shutdown or probe;
- `Starting` - the coordinator is attempting to wake and establish readiness;
- `Online` - readiness checks confirm the target is ready for backup traffic;
- `Stopping` - an authenticated shutdown has been requested and the coordinator is waiting for the node to become unavailable;
- `Faulted` - the most recent lifecycle attempt could not establish the desired state.

Runtime state is operational evidence, not a source of desired state. Desired state is derived from leases.

## Lifecycle invariant

For each target:

- one or more held leases means desired state is online;
- zero held leases means desired state is offline.

The gateway never reports a target usable to a client merely because a lease exists. A client may start backup data transfer only while it owns a held lease and the target is currently observed as `Online`.

If the desired state changes during a lifecycle transition, the coordinator converges again after the current side effect reaches a safe boundary. For example, a lease acquired after shutdown has already been issued may cause the target to finish powering off and then be woken again. Correctness does not depend on being able to cancel a shutdown command once sent.

## Coordination model

The initial deployment supports one active application instance. Within that instance, all lifecycle reconciliation for the same target is serialized by a keyed asynchronous coordinator. Different targets may be reconciled concurrently.

Durable state is still committed to PostgreSQL before correctness depends on it. In-process serialization is therefore an execution-order mechanism, not the authoritative state store.

A target reconciliation pass follows this shape:

1. Open a short WKG-managed database transaction and read durable leases/runtime metadata needed to compute desired state.
2. Commit the transaction before performing Wake-on-LAN, readiness checks, SSH, or other network I/O.
3. Perform at most the required lifecycle transition for the observed desired state.
4. Persist the resulting observation and audit outcome in another short transaction.
5. Re-evaluate desired state because leases may have changed while network I/O was in progress.

No database transaction remains open while waiting for a host to boot or shut down.

Supporting multiple active gateway replicas would require replacing the process-local per-target coordinator with distributed ownership/locking semantics. That is intentionally not implicit in the first release.

## Request model and idempotency

Lease identifiers are chosen by the client before sending an acquisition request. This allows the lease resource itself to provide idempotency instead of requiring a separate ephemeral idempotency cache.

Conceptually, the API exposes:

- `PUT /api/v1/targets/{targetId}/leases/{leaseId}` - create or return the caller's lease;
- `GET /api/v1/targets/{targetId}/leases/{leaseId}` - inspect lease ownership/freshness and target state;
- `POST /api/v1/targets/{targetId}/leases/{leaseId}/heartbeat` - update liveness metadata;
- `DELETE /api/v1/targets/{targetId}/leases/{leaseId}` - release the lease;
- administrator endpoints for force release and security administration.

The acquire request is not required to keep an HTTP connection open while a machine boots. Creation of the durable lease causes reconciliation to run; the client polls the lease/target state until the target is online or faulted. This also makes long boot times and gateway restarts ordinary resource-state transitions rather than fragile long-running HTTP requests.

## Persistence model

PostgreSQL stores four categories of durable state.

### Identity and authorization

ASP.NET Core Identity owns users, password hashes, roles, and related authentication data. Backup clients are dedicated service identities. A domain grant associates an Identity user with a configured target identifier.

The application uses coarse roles for administrator versus backup-client capabilities and explicit target grants for resource authorization. A client role alone never grants access to every target.

### Leases

Lease records contain:

- lease identifier;
- target identifier;
- owning Identity user identifier;
- creation time;
- release/force-release time and actor where applicable;
- last heartbeat time;
- optional bounded client correlation metadata useful for operations.

Uniqueness constraints enforce idempotent ownership semantics in addition to application-level validation.

### Target runtime observations

A runtime row per configured target records the latest lifecycle state, observation timestamp, last successful readiness time, and useful failure metadata. Persisted observations support diagnostics and recovery, but a process restart treats their freshness as unknown until reconciliation probes the target again.

### Audit events

Audit events are append-only records of security-sensitive and lifecycle-significant actions. State changes and their corresponding intent audit records should be persisted in the same transaction where practical. External side effects generate outcome events after the attempt completes.

## EF Core and transaction boundary

The application follows the WKG framework patterns used by `wkg-framework-demo`:

- the DbContext derives from the Identity EF Core context;
- WKG source-generated model discovery loads domain mappings;
- entity naming and property mapping are explicit and validated by policy;
- controllers/application services execute database work through WKG transaction scopes;
- read-only operations use read-only transaction helpers where appropriate;
- mutation paths explicitly commit or roll back through the transaction abstraction.

Read-committed isolation is sufficient for the initial single-instance deployment because target mutation ordering is serialized by the coordinator and database uniqueness constraints protect idempotency. The design must not silently claim multi-instance correctness from that assumption.

## Authentication and authorization flow

Service identities authenticate with a high-entropy credential managed by ASP.NET Core Identity. The token endpoint validates the credential and emits a short-lived signed JWT containing stable identity and role claims.

Target authorization remains database-backed. The presence of a target identifier in the request or token is not trusted as a grant. Lifecycle endpoints resolve the authenticated Identity user and check the current target grant before reading or mutating target-specific state.

The first administrator is bootstrapped only when the Identity store is empty. Bootstrap credentials are supplied through deployment secrets and are not persisted in plaintext.

## Lifecycle transports

### Wake-on-LAN

Wake-on-LAN sends a magic packet to the configured destination for the target. The transport is narrow: clients cannot select arbitrary MAC addresses, destinations, or payloads.

After sending Wake-on-LAN, the coordinator waits for the configured readiness probe rather than assuming packet delivery means the target is usable.

### Readiness

The initial readiness signal is a bounded network probe for the service required to begin backup access, typically the target SSH endpoint. Probe intervals and overall timeout are target configuration.

Readiness is a lifecycle signal only. A target is `Online` when the service required to begin backup access is reachable; broader host monitoring remains the responsibility of the existing Prometheus deployment.

### Shutdown

Shutdown connects to the configured target using a dedicated SSH identity and a fixed configured command. The SSH server host key must match the pinned configured fingerprint. Backup clients never provide commands or SSH connection parameters.

A successful command invocation transitions the target to `Stopping`; the gateway then probes until the target is unavailable or the configured shutdown timeout expires.

## Crash and partition behavior

### Client disappears

Heartbeats eventually mark the lease stale, but the lease remains held. The target stays online. An administrator may investigate and force-release the lease if the backup is known to be inactive.

### Gateway restarts with held leases

Leases remain authoritative in PostgreSQL. On startup, the gateway treats target observations as unknown and reconciles every configured target with held leases toward `Online` before clients rely on readiness state.

### Gateway restarts with no held leases

The gateway reconciles configured targets toward `Offline`. Because configured targets are dedicated nodes under gateway lifecycle ownership, unexpected online state is converged back to the expected powered-off state.

### Failure between state commit and external side effect

Reconciliation is level-triggered rather than dependent on a one-shot in-memory command. After restart or retry, the coordinator re-derives desired state from leases and repeats safe lifecycle actions as necessary. Wake-on-LAN is naturally repeatable; shutdown handling must tolerate the target already stopping or offline.

## Observability boundary

Prometheus metrics describe the gateway and the lifecycle state it directly owns. Durable audit events answer who requested or forced a security-sensitive change. Structured logs explain transient failures and include correlation identifiers that link API requests, reconciliation, and audit records.
