# API v1 contract

[`openapi-v1.yaml`](openapi-v1.yaml) is the checked-in compatibility contract for the first Backup Gateway API. The running application serves the same embedded document at `/openapi/v1.yaml` so Cyborg integration can consume a contract tied to the deployed binary rather than repository state.

The v1 compatibility boundary is intentionally small. Backup clients authenticate, acquire a caller-generated lease UUID, poll the returned lease/target state until the target is `Online`, heartbeat while the backup is active, and explicitly release the lease afterward. Backup data never traverses the gateway.

HTTP status is the stable error contract. Clients must not depend on framework-generated error text. JSON problem details may include a server-generated `correlationId` for diagnostics, but error messages are not machine-readable protocol state. The exception is heartbeat `409`, which deliberately returns the current `LeaseResponse` so a client can observe that its lease has already reached a terminal state.

A lease acquisition is idempotent only for the same authenticated client, target, and lease UUID. Reusing that UUID for a different client or target returns `409`. A held lease remains authoritative even when `isStale` is true; clients must never interpret staleness as automatic release.
