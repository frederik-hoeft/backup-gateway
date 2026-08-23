# Target Lifecycle Coordination

Backup Gateway derives target power intent from durable held leases and reconciles each configured target toward that intent. Lifecycle execution is deliberately level-triggered: correctness depends on current lease state and observed target reachability, not on an in-memory sequence of commands surviving process restarts.

## Desired and observed state

A target has only two desired states:

- `Online` when at least one durable lease is held;
- `Offline` when no durable lease is held.

The persisted runtime observation records `Unknown`, `Offline`, `Starting`, `Online`, `Stopping`, or `Faulted`. It is diagnostic state, not authority for whether a machine may be shut down. Held leases are always authoritative.

Every reconciliation pass reads desired state through a short database transaction, performs external I/O only after that transaction has completed, persists the resulting observation through another short transaction, and then re-evaluates desired state. This prevents network timeouts from holding database transactions open.

## Per-target serialization

Lease mutations and lifecycle execution use separate keyed serializers. Acquire/release requests therefore do not wait behind target boot or shutdown timeouts, while actual lifecycle side effects for one target remain serialized. Different targets may reconcile concurrently.

The reconciliation queue is level-triggered rather than relied upon for uniqueness. Duplicate queue entries are safe. Lease mutations enqueue the affected target, all configured targets are enqueued once when the periodic reconciliation service starts, and the service re-enqueues them at a bounded interval as a recovery mechanism.

## Wake and readiness

When a target is desired online but is not ready, the gateway records `Starting` and emits the configured Wake-on-LAN magic packet. The packet uses only the configured destination, UDP port, and MAC address.

Wake-on-LAN delivery is not treated as readiness. The gateway performs the configured bounded TCP probe until the target becomes reachable or the overall readiness timeout expires. Only a successful readiness probe permits the target to be recorded as `Online`.

If all leases disappear after Wake-on-LAN has already been emitted, the gateway still lets the target reach a safe observable boundary. If the target becomes ready, the next reconciliation step shuts it down; if it never becomes reachable and the desired state is already offline, the target can converge directly to `Offline` without turning an obsolete wake attempt into a lifecycle fault.

## Shutdown and SSH identity

Shutdown uses the configured SSH endpoint, username, private key, and fixed command. None of those values are accepted from lifecycle API callers.

Before each shutdown attempt, `ssh-keyscan` obtains the target's presented host key. The gateway computes the OpenSSH SHA-256 fingerprint of each returned key and proceeds only when one exactly matches the configured pinned fingerprint. The matching key is written to a temporary known-hosts file used for that invocation only.

The subsequent `ssh` process uses batch mode, public-key-only authentication, `IdentitiesOnly=yes`, strict host-key checking, the temporary known-hosts file, and the configured dedicated private key. The container image includes the OpenSSH client but continues to run the application itself as the non-root `app` user.

After the shutdown command returns successfully, the gateway waits until the configured readiness probe remains unavailable before recording `Offline`. If a lease appears after shutdown is already in progress, the gateway does not report the target usable while it is stopping. It lets the stop complete and then wakes the target again.

## Failures and restart recovery

Transport operations have bounded connection, command, readiness, and shutdown timeouts. A transport or reconciliation failure records `Faulted` with a bounded failure code; arbitrary SSH/process output is not persisted as lifecycle state.

A restart does not trust a previously persisted `Online` or `Offline` observation as proof of current reachability. Startup reconciliation probes every configured target and derives intent again from durable leases. Repeated Wake-on-LAN packets and repeated shutdown reconciliation are therefore expected recovery behavior rather than one-shot commands whose loss would leave the system inconsistent.
