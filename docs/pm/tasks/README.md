# Implementation Tasks

Each task corresponds to an incremental feature branch. The branches are stacked in order unless a task explicitly states otherwise. Every branch must build independently on its parent and include tests for its newly introduced behavior.

| Task | Branch | Primary outcome |
| --- | --- | --- |
| [01](01-project-foundation.md) | `feature/01-project-foundation` | Modern .NET solution and development/runtime baseline |
| [02](02-persistence-domain.md) | `feature/02-persistence-domain` | PostgreSQL/Identity persistence and explicit WKG EF model |
| [03](03-authentication-authorization.md) | `feature/03-authentication-authorization` | JWT service identities, admin provisioning, target grants |
| [04](04-target-configuration.md) | `feature/04-target-configuration` | Validated target/WOL/readiness/SSH configuration |
| [05](05-lease-coordination.md) | `feature/05-lease-coordination` | Durable idempotent lease API and concurrency invariants |
| [06](06-lifecycle-transports.md) | `feature/06-lifecycle-transports` | WOL/readiness/shutdown transports and reconciliation |
| [07](07-audit-observability.md) | `feature/07-audit-observability` | Durable audit trail, metrics, health, diagnostics |
| [08](08-production-hardening.md) | `feature/08-production-hardening` | Crash/concurrency hardening and production deployment readiness |

## Task discipline

A task is complete when:

- the repository builds and tests pass;
- public behavior introduced by the task has automated coverage;
- failure paths are tested when they affect lifecycle correctness or security;
- documentation reflects the steady-state system after the task;
- no unrelated refactoring is mixed into the branch;
- the branch can be reviewed as a coherent unit without requiring later branches to make its behavior safe.
