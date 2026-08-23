# Task 01: Project Foundation

Branch: `feature/01-project-foundation`
Parent: `feature/00-pm-architecture`

## Goal

Create the minimal modern .NET repository structure on which all later work depends.

## Work

- Add a `.slnx` solution targeting .NET 10/C# 14.
- Add the ASP.NET Core gateway project plus unit and integration test projects.
- Adopt the repository `.editorconfig`/build conventions from `frederik-hoeft/csharp-syle-guide` and the relevant organization conventions from Cyborg.
- Enable nullable reference types, analyzers, deterministic builds, and warnings appropriate for new production code.
- Establish `Source/` project organization consistent with the reference repositories.
- Add baseline health endpoint and empty application startup composition.
- Add Dockerfiles and a development Docker Compose baseline for the API and PostgreSQL without implementing domain persistence yet.
- Add test infrastructure placeholders only where they provide immediate value; avoid speculative abstractions.

## Acceptance criteria

- `dotnet build` succeeds from the solution root.
- `dotnet test` runs the test projects successfully.
- the application starts on .NET 10 and exposes a liveness endpoint;
- the API container runs as a non-root user;
- Docker Compose starts the gateway and PostgreSQL with secrets/configuration supplied outside source code;
- code formatting and analyzer configuration match the selected style guide.
