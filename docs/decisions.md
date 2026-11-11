# Decisions

Choices made while building RSVP Desk, with the reason. Newest at the bottom.
"Open" items are questions for the project owner; the build continues on the stated default until answered.

## Step 1: Skeleton

### D-001 .NET 10 (LTS)
.NET 11 is the newest release, but it is a standard-term (STS) release. The spec asks for the current LTS, which is .NET 10. `global.json` pins SDK 10.0.1xx with `latestFeature` roll-forward. EF Core and ASP.NET Core packages are pinned to 10.0.x to match.

### D-002 Razor Pages for the dashboard
Razor Pages over Blazor Server: plain server-rendered HTML, no SignalR circuit per user, easy to test with `WebApplicationFactory`, and cheap to host for the demo. The simulator panel will use a small amount of vanilla JavaScript (fetch + polling, possibly Server-Sent Events later). Bootstrap and jQuery from the template were removed; the site uses one small hand-written stylesheet.

### D-003 Solution layout
```
src/RsvpDesk.Engine        conversation engine, zero package references
src/RsvpDesk.Web           ASP.NET Core app: pages, minimal APIs, EF Core, channels, workers
tests/RsvpDesk.Engine.Tests
tests/RsvpDesk.Web.Tests   integration tests via WebApplicationFactory
```
The engine project has no dependencies at all, which enforces "no WhatsApp, HTTP or database types" at compile time.

### D-004 One model, one migration set per database provider
The spec says SQLite and PostgreSQL "must work through the same migrations". EF Core migrations are provider-specific (column types, identity strategy, annotations), so a single migration set cannot be correct for both. The approach EF Core documents for this is used instead:

- One model (`RsvpDbContext`, configured once).
- Two thin subclasses, `SqliteRsvpDbContext` and `PostgresRsvpDbContext`, each owning a migration folder (`Data/Migrations/Sqlite`, `Data/Migrations/Postgres`).
- Every model change adds a migration to both. CI runs `dotnet ef migrations has-pending-model-changes` for both contexts so one cannot drift from the model, and a test applies the PostgreSQL migrations to a real PostgreSQL in CI.

Adding a migration:
```
dotnet ef migrations add <Name> --project src/RsvpDesk.Web --context SqliteRsvpDbContext   -o Data/Migrations/Sqlite
dotnet ef migrations add <Name> --project src/RsvpDesk.Web --context PostgresRsvpDbContext -o Data/Migrations/Postgres
```
**Open:** confirm this meets the intent of "same migrations".

### D-005 Configuration
- `Database:Provider` = `Sqlite` (default) or `Postgres`; `ConnectionStrings:Rsvp`.
- `Database:MigrateOnStartup` = `true` by default, so the demo and `docker compose up` need no manual step. A real deployment can turn it off and run migrations as a release step.
- `Channel:Mode` = `Simulator` (default) or `WhatsApp`. Not wired up until step 3.
- Secrets: user-secrets locally (`UserSecretsId` is set), environment variables in deployment. None in the repository.

### D-006 Docker
Multi-stage build on the official `sdk:10.0` and `aspnet:10.0` images, running as the non-root `app` user on port 8080. The SQLite file lives on a `/data` volume. The runtime image has no curl, so `HEALTHCHECK` runs the app binary with `--healthcheck-probe`, which GETs `/health` and exits 0 or 1.

`docker compose up --build` runs SQLite. `DATABASE_PROVIDER=Postgres RSVP_CONNECTION_STRING="Host=postgres;Database=rsvpdesk;Username=rsvpdesk;Password=local-dev-only" docker compose --profile postgres up --build` runs against PostgreSQL.

### D-007 HTTPS behind a reverse proxy
The app redirects to HTTPS and sends HSTS outside Development. In a container, TLS usually terminates at a proxy. Forwarded headers are only trusted when `ReverseProxy:TrustForwardedHeaders=true`, because trusting them blindly would let a client spoof its IP address, which would undermine login rate-limiting later. Narrowing the trusted proxy list is part of step 8.

### D-008 Health check
`GET /health` returns `{"status":"Healthy","checks":{"database":"Healthy"}}` and never includes exception details.
