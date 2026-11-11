# RSVP Desk

An event management web app where guests receive an invitation and RSVP through a WhatsApp chatbot: attending or not, party size, a dietary requirement per person, wheelchair access and a free-text note. Organisers see live numbers, a catering report and an access report, and can download CSV files.

This is a portfolio project. Every event and guest in the demo is fictional.

> **Status:** in progress. Build step 1 (skeleton) is done. See [Build progress](#build-progress).

## Two modes

| Mode | What happens | Needs |
|---|---|---|
| **Simulator** (default) | Guest messages are typed into a phone-style panel in the browser. | Nothing. |
| **WhatsApp** | Real messages via the Meta WhatsApp Cloud API. | Your own Meta app, a test number and an approved message template. |

Simulator mode is the default so anyone can try the app without a Meta account.

## Architecture

The conversation logic knows nothing about WhatsApp.

```
Guest (WhatsApp)                         Browser (simulator)
      |                                        |
WhatsApp Cloud API  --webhook-->  WhatsAppChannel      SimulatorChannel
                                        \                /
                                      ConversationEngine   (pure, no I/O)
                                                |
                                  Database (guests, RSVPs, messages)
                                                |
                                  Dashboard (staff login)
```

- `src/RsvpDesk.Engine`: the conversation engine. A pure function from (state, inbound message, event details) to (new state, outbound messages, captured answers). It has no package references at all.
- `src/RsvpDesk.Web`: ASP.NET Core (Razor Pages and minimal APIs), EF Core, channels and background workers.

Design choices are recorded in [docs/decisions.md](docs/decisions.md).

## Run it

With Docker:

```bash
docker compose up --build
# http://localhost:8080 , health at http://localhost:8080/health
```

With the .NET 10 SDK:

```bash
dotnet tool restore
dotnet run --project src/RsvpDesk.Web
```

Tests:

```bash
dotnet test
```

The PostgreSQL migration test runs only when `RSVP_TEST_POSTGRES` holds a connection string (CI sets it).

### Database

SQLite by default (a file, good for the demo). For PostgreSQL:

```bash
export Database__Provider=Postgres
export ConnectionStrings__Rsvp="Host=localhost;Database=rsvpdesk;Username=...;Password=..."
```

Migrations are applied on startup unless `Database__MigrateOnStartup=false`.

## Build progress

1. [x] Skeleton: solution, Dockerfile, CI, health check, migrations
2. [ ] Conversation engine with table-driven tests
3. [ ] Persistence and simulator mode
4. [ ] Dashboard
5. [ ] Reports and CSV exports
6. [ ] Invitations and reminders
7. [ ] Live WhatsApp mode
8. [ ] Hardening and polish

## Privacy

Dietary and access needs can be special personal information under POPIA (health, religion). The app collects only what catering and access planning need, honours STOP immediately, and will delete or anonymise guest data a configurable number of days after the event.
