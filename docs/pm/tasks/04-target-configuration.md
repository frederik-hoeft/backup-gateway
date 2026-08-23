# Task 04: Target Configuration

Branch: `feature/04-target-configuration`
Parent: `feature/03-authentication-authorization`

## Goal

Define a narrow, validated operational configuration contract for targets without introducing runtime target administration.

## Work

- Add strongly typed target configuration options keyed by stable target identifier.
- Model WOL MAC/destination, readiness probe, SSH shutdown, timeout, retry, and secret-file references.
- Validate duplicate/invalid target identifiers, malformed MAC/IP/host-key fingerprints, missing key files, invalid timeouts, and unsafe/empty shutdown configuration at startup.
- Reject configuration that disables SSH host-key verification.
- Expose target lookup through an immutable application service rather than injecting raw configuration throughout the codebase.
- Reconcile configured target IDs with persisted runtime rows/grants without deleting historical audit data when configuration changes.
- Document the JSON/environment variable configuration shape and secret mounting model.

## Acceptance criteria

- invalid target configuration prevents readiness/startup with actionable errors;
- callers cannot override transport parameters through lifecycle API requests;
- configured private key material is referenced from external secret files and is never persisted in PostgreSQL;
- target identifiers are stable and suitable for durable authorization-grant references;
- configuration tests cover malformed and security-sensitive values.
