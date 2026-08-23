# Task 06: Lifecycle Transports and Reconciliation

Branch: `feature/06-lifecycle-transports`
Parent: `feature/05-lease-coordination`

## Goal

Connect lease-derived desired state to real target lifecycle operations while keeping external I/O isolated behind testable transport contracts.

## Work

- Implement Wake-on-LAN magic-packet transport using configured target values only.
- Implement bounded readiness probing.
- Implement SSH shutdown with dedicated private-key authentication and pinned server host-key verification.
- Implement target runtime state transitions: `Unknown`, `Offline`, `Starting`, `Online`, `Stopping`, and `Faulted`.
- Trigger reconciliation on lease state changes and at application startup; include a bounded periodic safety reconciliation.
- Re-evaluate desired state after every external side effect before deciding the next transition.
- Make repeated WOL/probe/shutdown handling tolerant of restart and already-transitioned targets.
- Keep network waits outside database transactions.
- Add transport unit tests and coordinator integration tests using deterministic fakes.

## Acceptance criteria

- a first held lease wakes an offline target and only reports `Online` after readiness succeeds;
- additional leases do not cause redundant shutdown/wake cycles;
- releasing the last lease requests shutdown and confirms offline state;
- an acquire during `Stopping` eventually converges back to `Online` without ever reporting readiness while the target is unusable;
- startup reconciliation respects durable held leases after a process restart;
- host-key mismatch aborts SSH shutdown and records fault state rather than bypassing verification;
- lifecycle failures are bounded by configured timeouts and do not hold database transactions open.
