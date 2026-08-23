# Initial Production Requirements

## Product goal

Backup Gateway is the authoritative lifecycle coordinator for dedicated backup target nodes. Backup clients such as Cyborg ask the gateway for permission to use a target instead of directly performing Wake-on-LAN or remote shutdown. The gateway owns target power-state coordination, authenticates callers, enforces target-level authorization, and records lifecycle activity for audit and operations.

The gateway does not proxy backup traffic. Once a target is ready, the backup source connects directly to the target for Borg or other backup data transfer.

## Initial deployment assumptions

The initial production release has deliberately narrow deployment constraints:

- one active Backup Gateway application instance;
- one PostgreSQL instance used by the gateway and ASP.NET Core Identity;
- both services deployed with Docker Compose;
- TLS terminates at a trusted reverse proxy or ingress in front of the application;
- the gateway host has network access required to send Wake-on-LAN packets, probe target readiness, and perform authenticated shutdown;
- configured backup targets are dedicated nodes whose power lifecycle is owned by the gateway.

Horizontal gateway replicas are out of scope until per-target coordination is made distributed. Running multiple active application instances is therefore unsupported in the initial release.

## Functional requirements

### Authentication

- API callers authenticate as ASP.NET Core Identity users.
- Backup clients use dedicated service identities rather than shared credentials.
- Successful authentication produces a short-lived bearer token suitable for machine-to-machine API calls.
- Self-registration is not exposed.
- The first administrator can be bootstrapped from deployment secrets on an empty database; subsequent service identities are provisioned through administrator-only functionality.
- Credential material and bearer tokens must never be written to logs or audit records.

### Authorization

- A backup client may only acquire targets for which it has an explicit grant.
- Administrator identities may provision clients, rotate/reset credentials, and manage target grants.
- Authorization is evaluated server-side on every protected lifecycle operation; target identifiers supplied by callers are never trusted as proof of access.
- Authentication and authorization failures use standard HTTP semantics without revealing credentials or unrelated target information.

### Target configuration

- Operational target definitions use the standard ASP.NET Core configuration model so JSON configuration, environment variables, and deployment-secret references compose predictably.
- Each target has a stable, human-readable identifier used by the REST API and authorization grants.
- Initial target configuration includes:
  - target host/address;
  - Wake-on-LAN MAC address and destination;
  - readiness probe settings;
  - SSH shutdown endpoint, user, command, private-key reference, and pinned server host-key fingerprint;
  - lifecycle timeouts and retry policy values.
- Configuration is validated at application startup. Invalid target definitions prevent the gateway from becoming ready.
- Secret key material is mounted or injected by the deployment environment and referenced by configuration; private keys are not stored in the database.
- Runtime target administration and YAML-specific configuration support are not required for the initial release.

### Lease lifecycle API

A lease is the durable reservation that permits one authenticated client to use one target.

- Clients create leases using a caller-generated UUID so acquisition retries are naturally idempotent.
- Repeating the same acquisition for the same client, target, and lease identifier returns the existing lease instead of creating another reservation.
- Reusing a lease identifier for conflicting ownership or target data is rejected.
- A held lease prevents automatic shutdown of its target regardless of heartbeat freshness.
- Clients can query a lease and the current target lifecycle state before starting backup data transfer.
- Clients explicitly release leases when their backup work is complete.
- Releasing an already released lease is idempotent.
- When the final held lease is released, the gateway converges the target toward the powered-off state.
- When a new lease arrives while a target is stopping, the gateway must not report the target ready until it has returned to a usable online state. If shutdown can no longer be cancelled, the gateway may allow shutdown to complete and wake the target again.

### Client liveness and abandoned leases

- Clients can heartbeat held leases so operators can identify clients that may have disappeared.
- Missing heartbeats mark a lease stale for diagnostics only.
- A stale lease remains held and continues to prevent shutdown.
- The gateway must never infer that a backup is safe to terminate solely from missing client heartbeats, because backup traffic bypasses the gateway and may continue during an API/network partition.
- Administrators can explicitly force-release an abandoned lease. Force release is a security-sensitive audited action.

This safety rule intentionally prefers leaving a target powered on over risking premature shutdown of an active backup.

### Target lifecycle coordination

- The gateway derives desired target power state from held leases: at least one held lease requires the target online; zero held leases requires the target offline.
- Lifecycle side effects for a target are serialized so concurrent acquire/release requests cannot issue conflicting Wake-on-LAN and shutdown actions.
- Database transactions are used for durable state transitions, authorization state, and audit metadata, but are not held open across network I/O.
- Wake-on-LAN is followed by readiness probing before the target is reported online.
- Shutdown uses authenticated SSH with server host-key verification.
- Startup and shutdown failures are surfaced as target fault state and recorded for diagnostics.
- The coordinator retries/reconciles from durable state after application restart.

### Audit

- Lifecycle requests, resulting target transitions, administrator credential/authorization changes, and force-release operations produce durable audit events.
- Audit records include timestamp, actor identity, action, target and lease identifiers where relevant, correlation/request identifier, and outcome.
- Audit events are append-only from the application perspective.
- Normal structured application logs complement the audit store but do not replace it.

### Operational API surface

The initial release exposes versioned REST endpoints for:

- token issuance;
- administrator client provisioning and target-grant management;
- lease acquire/query/heartbeat/release;
- administrator force release;
- target status visible to authorized callers;
- liveness/readiness health checks;
- Prometheus metrics.

The exact request/response schemas are defined alongside implementation and become the compatibility contract for Cyborg integration.

## Reliability and recovery requirements

- Durable leases survive application restarts.
- On startup the gateway must not trust a previously persisted online/offline observation as current truth; configured targets are reconciled/probed before readiness is reported from stale state.
- A gateway crash between durable lease mutation and an external lifecycle action must converge safely after restart.
- Failures must prefer a false-positive reservation or powered-on target over a false-negative reservation that could permit unsafe shutdown.
- API mutation retries must not create duplicate leases or duplicate authorization grants.
- Cancellation of an HTTP request must not roll back already committed durable state or leave correctness dependent on the client connection remaining open.

## Security requirements

- All non-health API endpoints require authentication except the token endpoint.
- Target-level authorization is deny-by-default.
- JWT signing keys, client credentials, and SSH private keys are provided through deployment secrets/files, not source-controlled configuration.
- SSH server identity is pinned and verified; disabling host-key verification is not an acceptable production configuration.
- Database credentials use a dedicated PostgreSQL role with only the privileges required by the application.
- The application runs as a non-root container user and does not require Docker socket access or privileged container mode.
- Lifecycle commands are configured by administrators and are never supplied by backup clients.
- API request models use explicit validation and bounded input sizes.

## Observability requirements

The initial release exposes gateway-centric metrics sufficient to operate the coordinator:

- active/stale lease counts by target;
- target lifecycle state;
- Wake-on-LAN, readiness, and shutdown attempt counts/outcomes;
- startup/shutdown duration measurements;
- authentication/authorization and API request metrics where safe and useful;
- database and application health signals.

Proxying cached target S.M.A.R.T. data, free-space information, or arbitrary target Prometheus metrics is deferred until the lifecycle coordinator is stable.

## Technology and implementation constraints

- .NET 10 and C# 14.
- ASP.NET Core Web API.
- PostgreSQL with Npgsql and EF Core.
- ASP.NET Core Identity for principal and credential management.
- WKG ASP.NET Core transaction management for request/domain transaction boundaries.
- WKG Entity Framework Core model discovery and explicit model-mapping policies, following the patterns demonstrated by `wkg-framework-demo`.
- Modern `.slnx` solution format.
- Repository formatting and analyzer configuration based on `frederik-hoeft/csharp-syle-guide`.
- Tests use the same PostgreSQL provider as production for persistence/integration behavior.

## Explicit non-goals for the initial release

- backup scheduling;
- proxying or inspecting backup data traffic;
- Borg repository or encryption-key management;
- automatic force release of stale leases;
- high-availability or multi-replica gateway coordination;
- Kubernetes-specific deployment artifacts;
- web UI;
- runtime target discovery;
- runtime target configuration CRUD;
- cached S.M.A.R.T./disk-space/target-metrics proxying;
- generalized remote command execution;
- YAML-specific configuration support.
