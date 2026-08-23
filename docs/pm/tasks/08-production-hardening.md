# Task 08: Production Hardening

Branch: `feature/08-production-hardening`
Parent: `feature/07-audit-observability`

## Goal

Validate failure recovery, concurrency, security boundaries, and Docker deployment as one production-ready single-instance release.

## Work

- Add end-to-end Docker Compose smoke tests with PostgreSQL.
- Add restart tests around lease creation, release, WOL intent, readiness, and shutdown transitions.
- Add stress/concurrency tests for multiple clients sharing one target and independent clients using different targets.
- Verify database migration/recovery behavior from a clean deployment.
- Harden container filesystem/user/capability settings and document required network access.
- Document reverse-proxy/TLS expectations, secret mounts, backup/restore of the PostgreSQL state, and key/credential rotation.
- Add explicit startup guard/documentation that the initial release supports one active gateway instance only.
- Review public API error models and OpenAPI output for stable Cyborg integration.
- Perform final architecture/documentation coherence pass.

## Acceptance criteria

- process restart does not lose held leases or permit shutdown while a held lease exists;
- randomized concurrent acquire/release tests preserve lease invariants;
- the container runs without privileged mode, Docker socket access, or root;
- all required secrets can be rotated without rebuilding the image;
- deployment documentation is sufficient to reproduce a clean installation;
- the API contract is ready for a separate Cyborg integration branch/repository change.
