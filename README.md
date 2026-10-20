# VStorage

Internal file storage and delivery service for the V* Consumer APIs (VGoalsPlanner, VLibrary, ...).
Specification: `D:\AI\Deepseek\VStorageAPI_requirements_en_v2.md`.

## Layout

| Project | Purpose |
|---|---|
| `src/VStorage.Domain` | Entities, value objects, rules (no package references) |
| `src/VStorage.Application` | Use-case services (no MediatR), options, abstractions |
| `src/VStorage.Infrastructure` | EF Core / PostgreSQL, blob store, workers |
| `src/VStorage.Api` | Minimal API endpoints, authentication, HTTP concerns |
| `tests/*` | Domain, application and integration tests |

## Run (development)

- API: `http://localhost:5190`, `https://localhost:7190` (profile `https`).
- Keycloak: `http://localhost:8083/realms/VEco`.
- Health: `GET /health/live`.
- Secrets live in user-secrets only (`dotnet user-secrets` in `src/VStorage.Api`).

Further sections (database, Keycloak setup, consumers) are added by later stages.
