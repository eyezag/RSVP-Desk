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

## Step 2: Conversation engine

### D-101 Engine API
```csharp
var engine = new ConversationEngine();                    // uses MessageCatalog.English
EngineResult r = engine.Invite(state, eventDetails);      // invitation; NotInvited -> Invited
EngineResult r = engine.Handle(state, inbound, eventDetails);
OutboundMessage m = engine.Prompt(state, eventDetails);   // current question, for reminders and re-prompts
```
`EngineResult` = new `ConversationState`, outbound messages, captured data, and a `Duplicate` flag. `Handle` takes the event details as an argument (the spec's signature lists only state and inbound) because the engine needs the party maximum and the event name, date and venue for its messages, and must not look them up itself.

The state is a plain immutable record (step, current person index, invalid-answer streak, answers so far, recent inbound message ids). It round-trips through System.Text.Json and is meant to be stored in `Conversations.StateData`.

### D-102 No text in the engine
The engine emits message keys with arguments (`MessagePart`) and option ids (`Choice`). `MessageRenderer` turns them into text using `Resources/messages.en.json`, which holds all message text, option labels, the typed words that match each option (aliases), the keywords and number words. Adding a language means adding `messages.{lang}.json`. A test checks every key the engine can use has text, and that every message the engine can produce renders.

The engine still uses the catalog's *word lists* to understand typed answers, since those are language data too.

### D-103 Option ids
Stable ids that will also be the WhatsApp button and list row ids: `attend.yes|no`, `party.1..max`, `diet.none|vegetarian|vegan|halal|gluten_free|other`, `wheelchair.yes|no`, `wheelchairs.1..n`, `note.skip`, `change`. The channel decides whether a question's choices become reply buttons or a list (step 7, once the limits are confirmed).

### D-104 Behaviour the spec left open
- **Party of one and wheelchair:** if the party size is 1 and the answer is "yes", the engine records 1 wheelchair user and skips `WheelchairCount` (the only valid answer would be 1).
- **Party size and wheelchair count are offered as choices** (`party.1..max`) as well as typed numbers ("2" or "two"). Decimals, negatives and out-of-range numbers are invalid.
- **Dietary list position:** typing "2" at the dietary question picks the second option, as people do with numbered lists.
- **Typed button titles** (e.g. "Yes, I'll be there") match, for channels that send the title without an id.
- **Taps on a button from an earlier question** are treated as invalid input, not as an answer to the current question.
- **Keywords** must be the whole message: "STOP" opts out, "Please stop at the side entrance" is a note. STOP and HELP are recognised in every state, including inside free-text answers. Synonyms: STOP = stop, unsubscribe, stop all; CHANGE = change, update, edit.
- **CHANGE before finishing** is invalid input with its own hint ("You can reply CHANGE once you've finished answering") and counts towards the HELP suggestion.
- **HELP mid-conversation** acknowledges, flags the guest and repeats the current question so they can carry on; it also resets the invalid streak. After finishing it only acknowledges.
- **Messages after Confirmed/Declined** (other than CHANGE/HELP/STOP) get "already confirmed" plus the summary, or "not attending" plus the CHANGE hint. They do not count as invalid.
- **A guest who writes first** (state `NotInvited`) gets the invitation text and attend question as a normal reply.
- **Invitations to opted-out guests** produce nothing. Re-inviting a guest who is mid-conversation re-sends the invitation without touching their answers; whether to allow that is an app-level rule for step 6.
- **"Two invalid answers in a row"**: the HELP line is added from the second consecutive invalid answer onwards.
- **Free-text lengths** (60 and 120) are counted in characters as the guest sees them (text elements), so emoji count as one. Empty or whitespace-only text is invalid. A Skip in the note step is the Skip button or one of: skip, no, none, nothing, n/a.
- **The note's "Skip"** stores `null`.
- **Idempotency:** the state remembers the last 50 inbound message ids. A repeat returns the state unchanged, with no messages and no captures. The database's unique index on the provider message id (step 3/7) is the main guard; this is the second line.
- **After STOP** the engine is silent for every input. Answers given before STOP are kept. Re-enabling is an organiser action outside the engine.

### D-105 Captured data
Emitted as it happens: `AttendanceCaptured`, `PartySizeCaptured`, `DietaryCaptured(personIndex, dietary, note)`, `WheelchairUsersCaptured`, `AccessNoteCaptured`, `RsvpCompleted(attending)`, `AnswersCleared`, `OptedOut`, `NeedsAttention`. Persistence (step 3) applies these to the `Rsvps`, `RsvpPersons` and `Guests` tables.

### D-106 Provisional WhatsApp label limits
Labels were written to fit 20 characters for reply buttons and 24 for list rows, the limits I believe apply. These are **not yet confirmed** against Meta's documentation; that check is part of step 7 and will be recorded in `docs/whatsapp-integration.md`. The test that enforces them says so.

## Open questions

1. D-004: is one migration set per provider acceptable?
2. D-104: are the behaviour choices above what you want, especially skipping the wheelchair count for a party of one and accepting "2" as the second dietary option?
3. Section 7 says template approval takes time and should be submitted first. Drafting `docs/templates.md` needs the current Meta documentation, which I plan to read at the start of step 7 unless you want the template drafted sooner so you can submit it early.
