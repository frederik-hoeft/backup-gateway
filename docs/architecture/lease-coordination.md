# Lease Coordination

A backup lease is the durable reservation that prevents a target from being shut down while a backup client may still be using it. The gateway derives target power intent only from lease state; network observations and heartbeat freshness do not weaken that reservation.

## Lease contract

Clients choose a UUID for each lease and acquire it with:

```text
PUT /api/v1/targets/{targetId}/leases/{leaseId}
```

The UUID is the idempotency key. Repeating the same request as the same authenticated client for the same target returns the existing lease. Reusing the UUID for another client or target is a conflict. A released UUID is not recycled into a new held lease.

A client may inspect, heartbeat, or release only its own lease:

```text
GET    /api/v1/targets/{targetId}/leases/{leaseId}
POST   /api/v1/targets/{targetId}/leases/{leaseId}/heartbeat
DELETE /api/v1/targets/{targetId}/leases/{leaseId}
```

Target authorization is evaluated by the normal target-access policy before these endpoints run, and ownership is checked again against the persisted lease. This keeps a retained lease inaccessible to another client even if target grants later change.

## Held state and heartbeats

A lease in `Held` state always inhibits shutdown. `Leases:StaleAfter` controls when the API reports its heartbeat as stale; the supported range is one minute through one day and the default is 15 minutes.

Staleness is diagnostic only. Backup traffic does not traverse the gateway, so loss of API connectivity cannot prove that the backup stopped. A stale lease therefore remains `Held` until its owner releases it or an administrator explicitly force-releases it.

Normal release changes the durable state to `Released`. Administrative recovery changes it to `ForceReleased`, preserving the distinction in both lease state and audit history. Force release remains available for a persisted lease whose target has subsequently been removed from active configuration.

## Desired target state

The desired lifecycle state is level-triggered from the database:

- one or more `Held` leases -> `Online`;
- zero `Held` leases -> `Offline`.

Heartbeat timestamps, the currently observed target state, and previous lifecycle failures do not participate in this calculation. Consequently, a stale held lease cannot accidentally make the target eligible for shutdown.

## Concurrency boundaries

The initial deployment supports one active gateway process, enforced by the PostgreSQL advisory-lock deployment guard. Within that process, short lease mutations are serialized per target so concurrent acquisitions/releases cannot race the transition from the last held lease. Operations for different targets remain independent. The advisory lock prevents accidental concurrent instances but is not a substitute for distributed fencing or supported active-active replicas.

Lifecycle reconciliation uses a separate per-target serializer. This is intentional: network operations may take seconds or minutes and must not block a client from durably acquiring or releasing a lease. Lease mutations commit their database transaction first and then enqueue level-triggered reconciliation. Reconciliation opens its own dependency-injection scope and re-reads durable desired state.

Lifecycle reconciliation consumes the same queued boundary and performs the configured Wake-on-LAN, readiness, and SSH shutdown operations. Lease mutations never perform those side effects inline. Database transactions therefore never span long-running lifecycle I/O.

Queueing is advisory rather than authoritative. Durable leases remain the source of truth if a request is cancelled after commit or a reconciliation attempt fails. Reconciliation is safe to repeat because each pass derives intent again from current held leases rather than from an edge-triggered start/stop command.

## Administrative recovery

Administrators can explicitly force-release a held lease through:

```text
POST /api/v1/admin/targets/{targetId}/leases/{leaseId}/force-release
```

This endpoint is intended for cases where an operator has independently established that the client no longer uses the target. It is not an automatic stale-heartbeat cleanup mechanism.
