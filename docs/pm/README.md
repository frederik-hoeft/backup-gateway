# Product and Implementation Planning

This directory defines the initial production scope for Backup Gateway and the implementation sequence used to build it. The planning documents are intentionally narrower than the long-term ideas in the project README: the first release is a single-instance, Docker-deployed lifecycle coordinator that safely arbitrates concurrent backup clients.

For the steady-state architecture of the implemented MVP, start with the [architecture overview](../architecture/README.md). The documents here remain the product scope and delivery-plan reference.

## Documents

- [Requirements](requirements.md) defines the initial production contract, explicit non-goals, and quality requirements.
- [Architecture](architecture.md) defines the system model, lifecycle invariants, persistence boundaries, security model, and recovery behavior.
- [User stories](user-stories.md) captures the behaviors the initial release must support from client and operator perspectives.
- [Tasks](tasks/README.md) breaks the implementation into independently reviewable, stacked feature branches with acceptance criteria.

## Delivery model

Implementation work is split into small stacked branches. Each branch should leave the repository buildable and should contain the tests and documentation required for the behavior introduced by that branch. Later branches may depend on earlier branches when they build on shared infrastructure.

The initial branch stack is:

1. `feature/00-pm-architecture` - product scope, architecture, user stories, and implementation plan.
2. `feature/01-project-foundation` - .NET 10 solution, shared build conventions, test projects, and development container baseline.
3. `feature/02-persistence-domain` - PostgreSQL, Identity persistence, WKG EF Core model configuration, and core domain entities.
4. `feature/03-authentication-authorization` - service identities, JWT authentication, target grants, and administrator provisioning.
5. `feature/04-target-configuration` - validated target lifecycle configuration and secret references.
6. `feature/05-lease-coordination` - idempotent lease API and authoritative per-target coordination.
7. `feature/06-lifecycle-transports` - Wake-on-LAN, readiness probing, SSH shutdown, and lifecycle reconciliation.
8. `feature/07-audit-observability` - durable audit events, Prometheus metrics, health checks, and operational diagnostics.
9. `feature/08-production-hardening` - recovery tests, concurrency tests, container hardening, and release documentation.

The Cyborg-side integration should be implemented after the gateway API contract stabilizes. It is tracked as an integration follow-up rather than coupled to the gateway implementation stack.
