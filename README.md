# RSVP Desk

An event management web app where guests receive an invitation and RSVP through a WhatsApp chatbot: attending or not, party size, a dietary requirement per person, wheelchair access and a free-text note. Organisers see live numbers, a catering report and an access report, and can download CSV files.

This is a portfolio project. Every event and guest in the demo is fictional.

> **Status:** in progress. Build steps 1 to 6 are done: RSVP as a fictional guest in the simulator, then follow it on the staff dashboard and in the catering and access report. See [Build progress](#build-progress).

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

Every inbound message, from the simulator now and from WhatsApp later, takes the same path: stored raw (with a unique id, so repeats are ignored), queued, processed in the background by the engine, then answers, messages and conversation state are saved together and replies are sent through the active channel.

### The conversation

```
Bot:   You're invited to Year-End Function 2026! ... Will you attend?   [Yes, I'll be there] [No, I can't make it]
Guest: yes
Bot:   Great! How many people will attend, including you? (1 to 4)
Guest: 2
Bot:   Any dietary requirement for you?          [No requirements | Vegetarian | Vegan | Halal | Gluten-free | Other]
Guest: Other
Bot:   Please type the requirement, for example nut allergy.
Guest: Nut allergy
Bot:   Any dietary requirement for guest 2?
Guest: halal
Bot:   Does anyone in your party use a wheelchair?   [Yes] [No]
Guest: no
Bot:   Anything else we should know, ...? Tap Skip or type your note.   [Skip]
Guest: Skip
Bot:   Thank you, you're confirmed ... Reply CHANGE to update.
```

STOP, HELP and CHANGE work throughout. All bot text lives in `src/RsvpDesk.Engine/Resources/messages.en.json`.

Design choices are recorded in [docs/decisions.md](docs/decisions.md).

## Run it

With Docker:

```bash
docker compose up --build
# http://localhost:8080/Simulator , health at http://localhost:8080/health
```

The sign-in page shows the demo account. Open **Simulator**, pick a guest, click **Send invitation**, then answer as the guest with the buttons, the list or by typing. The right-hand panel shows what was saved. **Reset guest** starts over.

With the .NET 10 SDK:

```bash
dotnet tool restore
dotnet run --project src/RsvpDesk.Web
```

Tests:

```bash
dotnet test
```

The PostgreSQL migration test runs only when `RSVP_TEST_POSTGRES` holds a connection string. To run the integration tests against PostgreSQL instead of SQLite, also set `RSVP_TEST_DB=Postgres`. CI does both.

### Database

SQLite by default (a file, good for the demo). For PostgreSQL:

```bash
export Database__Provider=Postgres
export ConnectionStrings__Rsvp="Host=localhost;Database=rsvpdesk;Username=...;Password=..."
```

Migrations are applied on startup unless `Database__MigrateOnStartup=false`.

## Build progress

1. [x] Skeleton: solution, Dockerfile, CI, health check, migrations
2. [x] Conversation engine with table-driven tests
3. [x] Persistence and simulator mode
4. [x] Dashboard
5. [x] Reports and CSV exports
6. [x] Invitations, reminders and CSV import
7. [ ] Live WhatsApp mode
8. [ ] Hardening and polish

## Privacy

Dietary and access needs can be special personal information under POPIA (health, religion). The app collects only what catering and access planning need, honours STOP immediately, and will delete or anonymise guest data a configurable number of days after the event.
