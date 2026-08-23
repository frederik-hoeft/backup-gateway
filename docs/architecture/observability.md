# Audit and Observability

Backup Gateway separates durable audit history from operational telemetry. Audit records answer who or what caused security-sensitive state changes and lifecycle side effects. Metrics and logs describe current operation and transient failures. Target-host health and hardware metrics remain outside the gateway and continue to be collected directly by the existing Prometheus deployment.

## Correlation

Each HTTP request receives a server-generated UUID correlation identifier returned in `X-Correlation-ID`. The same identifier is used by audit events created during that request and is added to the structured logging scope. Client-provided correlation values are not accepted as identifiers, which keeps the field bounded and prevents untrusted text from entering logs or audit records.

Each background target reconciliation creates its own correlation identifier. The coordinator keeps that identifier in the logging scope for the complete serialized reconciliation and the lifecycle audit writer uses the same value for side-effect intent and outcome events.

## Durable audit

Audit events are append-only PostgreSQL records. They contain bounded scalar identifiers and event codes rather than navigational relationships whose deletion could erase history.

Lease acquisition/release, administrator force release, client creation and credential rotation, target-grant changes, bootstrap administration, lifecycle wake/shutdown intent and outcome, and reconciliation faults are durable audit events. Lifecycle transport output is never copied into audit details; failures use bounded internal failure codes instead.

Power-affecting lifecycle actions write their intent before performing the external side effect. A failed intent write therefore prevents the side effect. The corresponding success or failure outcome is written after the attempt through a separate short transaction.

Administrators can query recent bounded audit history through `/api/v1/admin/diagnostics/audit`. Target diagnostics at `/api/v1/admin/diagnostics/targets/{targetId}` expose current observed state, held/stale lease totals, and at most 100 lease identities so an operator can determine why a configured target remains reserved.

## Prometheus metrics

`/metrics` exposes a compact Prometheus text endpoint. The metric dimensions are intentionally bounded:

- configured target identifier;
- fixed lease freshness (`fresh` or `stale`);
- fixed lifecycle state;
- fixed lifecycle operation and outcome codes.

The endpoint reports held lease counts, one-hot target lifecycle state, lifecycle operation counters, and cumulative operation duration. It does not use credentials, JWTs, SSH material, usernames, lease UUIDs, exception messages, or arbitrary request values as labels.

Lifecycle operation counters are process-local and reset when the gateway restarts. Durable state and audit history remain in PostgreSQL.

## Health checks

`/health/live` is process liveness only and intentionally does not depend on PostgreSQL or target reachability. This allows the deployment platform to distinguish a running process from one that should be restarted.

`/health/ready` verifies PostgreSQL connectivity. Target configuration has already been fully validated before the application begins serving requests, so invalid lifecycle configuration prevents startup rather than producing a nominally ready process. Individual backup targets are not readiness dependencies for the gateway because powered-off targets are an expected state.

## Safe failure reporting

Structured lifecycle logs use target IDs and bounded internal failure codes. SSH process stdout/stderr is not included in lifecycle exceptions or persisted audit events. Authentication failures use a generic log message and response rather than echoing usernames or credentials.
