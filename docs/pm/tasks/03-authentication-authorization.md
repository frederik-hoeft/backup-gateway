# Task 03: Authentication and Authorization

Branch: `feature/03-authentication-authorization`
Parent: `feature/02-persistence-domain`

## Goal

Provide secure machine authentication and least-privilege target authorization using ASP.NET Core Identity.

## Work

- Define administrator and backup-client roles/policies.
- Implement empty-database administrator bootstrap from deployment secrets.
- Implement a token endpoint using Identity credential validation and short-lived signed JWTs.
- Load the JWT signing key from a mounted secret file; validate issuer, audience, lifetime, and signing algorithm.
- Add administrator-only service identity creation and credential rotation/reset.
- Add administrator-only target grant/revoke operations.
- Implement a reusable target authorization service/policy that checks the current database grant.
- Ensure target existence/grant errors do not leak unrelated resource information.
- Audit successful security administration; log authentication failures without persisting secrets.

## Acceptance criteria

- no public registration endpoint exists;
- bootstrap cannot overwrite or recreate administrator credentials once Identity contains users;
- backup-client tokens cannot invoke administrator endpoints;
- a client without a target grant cannot access that target's protected operations;
- grant revocation takes effect on subsequent authorization checks even for an otherwise valid JWT;
- tokens/credentials are absent from logs and audit records;
- authentication and authorization integration tests cover success, failure, expiry, and role/grant boundaries.
