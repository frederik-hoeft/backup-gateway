# Task 05: Lease API and Coordination

Branch: `feature/05-lease-coordination`
Parent: `feature/04-target-configuration`

## Goal

Implement the correctness-critical durable lease contract and concurrent lifecycle arbitration without yet performing real WOL/SSH side effects.

## Work

- Implement versioned lease acquire/query/heartbeat/release endpoints.
- Use caller-selected UUID lease identifiers for idempotent creation.
- Enforce lease ownership and target authorization on every operation.
- Implement stale-heartbeat classification without automatic release.
- Add administrator force-release endpoint.
- Add a keyed per-target asynchronous coordinator for the supported single-instance deployment.
- Derive desired target state exclusively from held leases.
- Define lifecycle reconciliation interfaces with fake/no-op transports so coordination can be tested independently from network side effects.
- Ensure HTTP cancellation after durable commit cannot erase or implicitly release a lease.
- Add concurrency tests for simultaneous acquire/release sequences.

## Acceptance criteria

- repeated acquisition of the same lease is idempotent;
- conflicting lease UUID reuse is rejected;
- no path can derive desired `Offline` while any held lease exists;
- stale leases still inhibit shutdown;
- force release is administrator-only and distinguishable from normal release;
- concurrent operations on one target are serialized while separate targets may proceed independently;
- database transactions do not span simulated long-running lifecycle I/O;
- tests reproduce concurrent scenarios with multiple service identities.
