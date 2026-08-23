# Task 07: Audit and Observability

Branch: `feature/07-audit-observability`
Parent: `feature/06-lifecycle-transports`

## Goal

Make lifecycle and security behavior explainable in production without turning observability into a general target-monitoring subsystem.

## Work

- Emit durable append-only audit events for lease actions, force release, security administration, and lifecycle side-effect intent/outcomes.
- Correlate HTTP requests, coordinator executions, logs, and audit records with stable correlation identifiers.
- Expose Prometheus metrics for lease counts/freshness, target state, lifecycle outcomes, and transition durations.
- Add liveness and readiness health checks that account for database connectivity and validated configuration.
- Add safe structured logging for lifecycle failures.
- Add administrator/operator query surface for recent audit/lifecycle diagnostics if required for practical operation; keep it read-only and bounded.
- Explicitly exclude credentials, JWTs, SSH key material, and unbounded user-supplied text from metric labels/audit payloads.

## Acceptance criteria

- an operator can determine why a target is currently being kept online;
- every force release and authorization change has a durable actor/outcome record;
- metrics use bounded-cardinality labels;
- health/readiness behavior distinguishes process liveness from ability to serve lifecycle requests;
- secret-scanning tests/log assertions cover representative authentication and SSH failures;
- target S.M.A.R.T./free-space proxying is not introduced by this task.
