# Initial User Stories

The initial release has two actors: a backup client and a gateway administrator/operator.

## Backup client

### US-01 Authenticate as a dedicated service identity

As a backup client, I can exchange my service identity credential for a short-lived bearer token so that I do not need direct network permission for target Wake-on-LAN or shutdown.

Acceptance criteria:

- invalid credentials do not produce a token;
- a valid backup-client identity receives a bounded-lifetime token;
- credentials and tokens are absent from logs and audit payloads;
- self-registration is unavailable.

### US-02 Acquire an authorized target

As a backup client, I can create a durable lease for a target I am authorized to use so the gateway will bring that target online and prevent shutdown while my lease is held.

Acceptance criteria:

- acquisition requires authentication and a target grant;
- the client provides the lease UUID;
- retrying the same request returns the same lease;
- a conflicting reuse of the UUID is rejected;
- the target begins converging toward `Online` when the first lease is held.

### US-03 Wait until the target is actually ready

As a backup client, I can query my held lease and target lifecycle state so I only begin backup traffic after the gateway has confirmed readiness.

Acceptance criteria:

- a newly woken target is not reported `Online` until its readiness probe succeeds;
- a target startup failure is visible as fault state with safe diagnostic information;
- the lease remains held while the target is starting or faulted unless the client releases it.

### US-04 Share a target with concurrent jobs safely

As a backup client, my lease can coexist with leases from other authorized clients so one job finishing cannot power down a target another job still uses.

Acceptance criteria:

- any held lease prevents shutdown;
- releasing one of several leases does not request shutdown;
- only transition to zero held leases makes `Offline` the desired state;
- concurrent acquire/release tests cannot produce an online-to-offline transition while a held lease exists.

### US-05 Release a completed backup idempotently

As a backup client, I can release my lease after the backup completes so the gateway can shut down an otherwise unused target.

Acceptance criteria:

- release requires ownership of the lease or administrator privilege;
- repeated release is safe and returns a consistent result;
- releasing the last held lease triggers reconciliation toward `Offline`;
- the API does not require the caller to keep a connection open until shutdown completes.

### US-06 Report liveness without risking unsafe cleanup

As a backup client, I can heartbeat a long-running lease so operators can distinguish healthy long jobs from possibly abandoned leases.

Acceptance criteria:

- heartbeat updates the lease freshness timestamp;
- missing heartbeat can mark a lease stale;
- stale status never releases the lease or permits automatic shutdown.

### US-07 Recover from gateway/API interruption

As a backup client, I can retry lease operations after an API timeout or gateway restart without creating duplicate reservations or losing an existing reservation.

Acceptance criteria:

- acquisition uses durable caller-selected identity;
- committed lease state survives process restart;
- request cancellation does not implicitly release a committed lease;
- target reconciliation resumes after restart.

## Administrator/operator

### US-08 Bootstrap and provision service identities

As an administrator, I can bootstrap the first administrator securely and provision dedicated service identities for backup sources so credentials are not shared between machines.

Acceptance criteria:

- bootstrap only applies to an empty Identity database;
- bootstrap secrets come from deployment secret configuration;
- subsequent client provisioning requires administrator authorization;
- client credentials can be rotated/reset without changing target configuration.

### US-09 Grant least-privilege target access

As an administrator, I can grant or revoke a client's access to individual configured targets so a compromised source cannot control unrelated backup nodes.

Acceptance criteria:

- grants are deny-by-default;
- grant changes are durable and audited;
- revoking a grant prevents future lifecycle operations;
- revocation does not silently release an already held lease; an administrator must explicitly force-release it if required.

### US-10 Diagnose lifecycle state

As an operator, I can see target state, active/stale lease counts, and recent lifecycle failures so I can determine why a target is online, offline, or faulted.

Acceptance criteria:

- target state is not inferred from stale pre-restart observations;
- metrics expose lifecycle state and operation outcomes;
- logs correlate API requests with reconciliation attempts without exposing secrets.

### US-11 Audit security-sensitive actions

As an operator, I can determine who acquired/released a target, who changed authorization, and what lifecycle side effects occurred so incidents and unexpected uptime can be reconstructed.

Acceptance criteria:

- audit records are durable and append-only from application code;
- events include actor, action, outcome, timestamp, and relevant target/lease/correlation identifiers;
- sensitive credentials, JWTs, and SSH keys are never recorded.

### US-12 Recover an abandoned lease explicitly

As an administrator, I can force-release a stale/abandoned lease after confirming the backup is no longer active so a target is not kept online indefinitely.

Acceptance criteria:

- force release requires administrator authorization;
- force release is distinct from ordinary client release in audit data;
- after the final held lease is force-released, normal target reconciliation applies.
