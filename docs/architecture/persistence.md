# Persistence Architecture

The Backup Gateway uses one PostgreSQL database for authentication state and all durable coordinator state. `BackupGatewayDbContext` is the persistence boundary and derives from the ASP.NET Core Identity context so Identity and domain changes can participate in the same database transaction when required.

## Model configuration

Domain mappings use WKG Entity Framework Core model discovery. `BackupGatewayModelLoader` is source generated from the `BackupGateway.Web` assembly, and the DbContext requires explicit entity names and property mappings. Domain entities derive from the common `BackupGatewayEntity` base model, which maps application-generated UUIDs as non-database-generated primary keys.

ASP.NET Core Identity retains its framework mappings. Identity users and roles use GUID primary keys so domain authorization state can reference principals without string-key conversion.

EF Core migrations are the schema authority. The application applies pending migrations before it begins serving requests. A design-time DbContext factory allows migration generation without loading deployment credentials or depending on the runtime configuration pipeline.

## Durable state

### Target grants

A target grant relates one Identity client to one configured target identifier. The `(client_id, target_id)` pair is unique, making target authorization deny-by-default and unambiguous.

Grants have a normal foreign key to the Identity user with cascade deletion. Removing a principal therefore removes authorization state that cannot have meaning without that principal.

### Backup leases

A backup lease is keyed by the caller-selected lease UUID. The primary key makes a lease identifier globally unique and prevents the same identifier from being inserted for a different client or target.

A lease records its client identifier as a scalar snapshot rather than as a foreign key to Identity. This is a safety boundary: deleting or revoking an Identity principal cannot implicitly delete a held lease and thereby make the target eligible for shutdown.

Heartbeat freshness is stored independently from lease state. Database constraints require held leases to have no release timestamp and released leases to have one, and prevent a heartbeat timestamp from predating lease creation. Staleness is derived from the heartbeat timestamp and never changes lease ownership automatically.

### Target runtime observations

At most one runtime observation exists for each configured target. It stores the latest lifecycle state and observation timestamp. The row is operational evidence only; leases remain the source of desired target state.

### Audit events

Audit events store immutable actor, target, lease, correlation, event-type, outcome, and bounded detail fields without foreign keys to mutable operational entities. Historical events therefore remain meaningful if the referenced client or other runtime state later disappears.

The DbContext rejects modified or deleted tracked audit events during `SaveChanges`. Normal application persistence paths can append audit events but cannot rewrite or remove them.

## Transaction boundary

WKG transaction management is configured at read-committed isolation. Database transactions protect short state transitions and authorization/lease invariants. They must not span Wake-on-LAN delivery, readiness polling, SSH, or other network waits.

The initial deployment has one active gateway instance, so process-local target reconciliation provides execution serialization while PostgreSQL remains the durable source of truth. The application enforces this deployment contract with a PostgreSQL session advisory lock acquired before migrations/startup initialization; losing the lock session causes the process to stop. The guard prevents accidental concurrent instances but does not provide the distributed fencing required for supported active-active replicas. Database uniqueness and check constraints protect invariants that must survive process restarts or duplicate requests.

## Testing boundary

Persistence integration tests run against PostgreSQL rather than an EF in-memory provider. The test suite uses a dedicated connection supplied through `BACKUP_GATEWAY_TEST_DATABASE`, recreates that database between persistence tests, applies the real migration set, and verifies uniqueness constraints, append-only audit enforcement, and WKG commit/rollback behavior.
