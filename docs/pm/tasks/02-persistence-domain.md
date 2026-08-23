# Task 02: Persistence and Domain Model

Branch: `feature/02-persistence-domain`
Parent: `feature/01-project-foundation`

## Goal

Establish the durable state model using PostgreSQL, ASP.NET Core Identity, and WKG Entity Framework Core conventions before exposing lifecycle behavior.

## Work

- Add Npgsql EF Core and ASP.NET Core Identity persistence.
- Introduce the gateway DbContext derived from the appropriate Identity DbContext.
- Add WKG source-generated model discovery.
- Require explicit entity naming/property mapping through WKG model policies.
- Add explicitly mapped entities for:
  - client-to-target authorization grants;
  - backup leases;
  - target runtime observations;
  - append-only audit events.
- Add database constraints for lease ownership/idempotency and grant uniqueness.
- Add the initial migration and startup migration application consistent with the reference WKG project.
- Configure WKG transaction management with read-committed isolation.
- Add integration tests against PostgreSQL for mappings, constraints, and transaction behavior.

## Acceptance criteria

- a clean PostgreSQL database migrates successfully on startup;
- Identity and domain tables coexist in one DbContext;
- WKG explicit-mapping policies reject unintentionally unmapped domain model additions;
- duplicate grants and conflicting lease identity are rejected by durable constraints;
- audit entities cannot be modified through normal domain paths after insertion;
- integration tests use PostgreSQL rather than an EF in-memory substitute.
