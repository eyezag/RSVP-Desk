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

## Step 3: Persistence and simulator mode

### D-201 Data model, and where it differs from spec section 8
All section 8 tables except `Users` and `AuditLog`, which arrive with login (step 4) and the audit log (step 8). Additions:

| Column | Why |
|---|---|
| `Events.TimeZoneId` | Times are stored in UTC (D-202); the venue's zone is needed to show 18:00 rather than 16:00. |
| `Events.IsSample` | Marks the fictional demo event; the simulator only works on it. |
| `Guests.LastActivityAt` | The dashboard's "last activity" column, without a query over all messages. |
| `Conversations.Version` | Optimistic concurrency, so an invitation and an inbound reply can't overwrite each other. |
| `Messages.ReplyId` | The button or list id the guest tapped, next to the text. |
| `Messages.ChoicesJson` | The options sent with a bot message, so the simulator and the guest detail view can show them. |
| `WebhookEvents.Source` | `simulator` or `whatsapp`, so the worker knows which channel normalises the raw JSON. |

Enums are stored as their names (readable, and safe to reorder). `Guests (EventId, PhoneE164)` is unique; `Messages.ProviderMessageId` and `WebhookEvents.ExternalId` have unique indexes.

### D-202 Dates and times
Everything is stored in UTC. SQLite has no date type and EF Core cannot compare or sort `DateTimeOffset` there, so the SQLite context stores UTC ticks as integers; the PostgreSQL context uses `timestamp with time zone` and converts values to UTC on write, as Npgsql requires. The event's local time comes from `Events.TimeZoneId` (IANA, e.g. `Africa/Johannesburg`). The integration suite runs on both databases in CI to catch differences.

### D-203 One ingest path for both channels
A simulator message is handled exactly as a WhatsApp webhook will be: it is stored as a raw `WebhookEvent` first, its id is put on an in-memory queue (`Channel<long>`), and the `InboundWorker` background service processes it: the channel normalises the raw JSON, `ConversationService` runs the engine and saves messages, answers and state in one `SaveChanges`, then sends the replies. Consequences:
- Live mode in step 7 only needs a `WhatsAppCloudChannel` and the webhook endpoint; the rest is already exercised by the demo.
- A single reader processes events in order, so two quick messages from the same guest can't race. That is plenty for a few hundred guests; partitioning by guest would be the next step if it weren't.
- On start-up the worker re-queues stored events that were never processed, so a restart loses nothing.
- Processing errors are written to `WebhookEvents.Error`; failed events are not retried automatically.
- `POST /api/simulator/{guestId}/messages?wait=true` waits up to 5 seconds for the worker, so the simulator feels instant without polling. Without `wait` it returns 202 at once.

### D-204 Idempotency in three layers
1. `WebhookEvents.ExternalId` is unique: a repeated delivery is answered "duplicate" and never queued.
2. `Messages.ProviderMessageId` is unique, and the service skips an inbound message it has already stored.
3. The engine ignores message ids it has seen (D-104).

### D-205 Sending
Outbound messages are saved as `Pending` in the same transaction as the state change, then sent through the active channel and marked `Sent` or `Failed`. Sending after the commit means a crash can leave a message `Pending` but never sends a message the database doesn't know about. Retries with back-off, and a sweep for stuck `Pending` messages, belong to step 7 where sends can actually fail.

### D-206 No login yet
`GET /api/guests/{id}` is a staff endpoint but is open until step 4 adds login. The simulator endpoints are guest-side and stay open by design (spec section 9); they only exist in simulator mode and only act on the sample event's guests. Starting the app with `Channel:Mode=WhatsApp` fails fast with "not implemented until step 7".

### D-207 Sample data
On an empty database the app creates "Year-End Function 2026" (Sat 5 Dec 2026 18:00, Johannesburg, capacity 60, party max 4) and 12 fictional guests. Their numbers are in the unallocated `+27 00…` range, so they can never reach a real phone. One guest has no consent recorded, to show that such guests can't be invited. Turn seeding off with `Demo:SeedSampleEvent=false`. The simulator's **Reset guest** button (`POST /api/simulator/{id}/reset`) puts a guest back to "not invited" so the demo can be repeated.

### D-208 Invitations in this step
Only a single invitation, from the simulator. It needs consent and no opt-out, and is allowed while the guest is not yet invited or still at the attend question (a resend). Bulk sending, reminders and their rules are step 6.

### D-209 Guest status
Derived after every change and cached on `Guests.Status`: no conversation = NotInvited; invitation sent and no reply = Invited; any reply before finishing (including after CHANGE) = InChat; finished = Attending or Declined. Opting out does not change the status; `OptedOutAt` is shown separately and earlier answers are kept.

### D-210 Simulator panel
A Razor page with plain JavaScript (`wwwroot/js/simulator.js`). Bot messages with up to 3 options show reply buttons; more show a "Choose" button that opens a list, mirroring WhatsApp (limits still provisional, D-106). Old buttons stay tappable, as on WhatsApp, which shows the engine's stale-button handling. The "Saved answers" panel reads back from `GET /api/guests/{id}`, so it shows what is in the database, not what the page thinks.

### D-211 Running locally
`dotnet run` uses the Development profile. Running an unpublished build with `ASPNETCORE_ENVIRONMENT=Production` serves empty compressed static files (a .NET static-assets behaviour: compressed variants only exist in the publish output), so use Development locally or run the Docker image, which is published.

### D-212 Finding the guest for a live message
WhatsApp messages carry a phone number, not a guest id. For now the most recent event's guest with that number is used. Step 7 will revisit this, since one person may be on several events' lists.

## Step 4: Dashboard

### D-301 Staff login
A small `Users` table (email, password hash, role, failed-login count, lock-out time) with ASP.NET Core Identity's `PasswordHasher` and cookie authentication, rather than full ASP.NET Core Identity: the app needs two roles and a login form, nothing more.
- Cookie: HttpOnly, SameSite=Strict, Secure in Production, 8-hour sliding session.
- Five wrong passwords lock the account for 15 minutes. Every failure shows the same message, and unknown emails still run a password hash, so the page doesn't reveal which accounts exist.
- The login page is rate-limited per client IP (20 requests a minute by default, `Auth:LoginRateLimitPerMinute`).
- All pages and APIs need a signed-in user unless they opt out (a fallback policy). Public: home, simulator page and its API, sign-in, `/health`, static files. Staff APIs answer 401/403 instead of redirecting.
- Forms carry antiforgery tokens; the return URL after sign-in must be local.
- Passwords are at least 12 characters.

### D-302 Creating users
The first organiser comes from configuration (`Auth:InitialOrganiser:Email` / `Password`, via user-secrets or environment variables) and is created only when there are no users. More users: `dotnet RsvpDesk.Web.dll --create-user <email> <Organiser|Viewer>`, with the password in `RSVP_NEW_USER_PASSWORD` or typed in. A user-management page can come later.

### D-303 Demo sign-in
For the public demo, `Demo:ShowLoginHint=true` shows the initial organiser's credentials on the sign-in page. It is ignored in live WhatsApp mode. `docker compose` turns it on with a demo account; Development uses `organiser@rsvpdesk.example` / `local-dev-password`.

### D-304 Roles
- **Organiser:** everything.
- **Viewer:** read-only. Sees the summary tiles (except wheelchair users), the guest table without the dietary and access columns, and guest details without dietary and access answers or the conversation (which repeats them). The API returns those fields as `null`. Dietary and access needs are special personal information under POPIA, so they are limited to the people planning catering and access.

### D-305 Audit log in this step
The `AuditLog` table arrives now (not in step 8) because logins and guest views should be recorded from the start. Recorded so far: logins and failures, lock-outs, an organiser opening a guest's details, adding and editing guests and events, marking help handled, re-enabling messages. Targets are ids (`guest:12`), never names or numbers.

### D-306 Dashboard behaviour
- Status filter options: all, each status, "awaiting reply" (invited or in chat), "needs attention", "opted out". Search matches names (case-insensitive) and, when the search has three or more digits, phone numbers. 25 guests per page, sorted by name.
- Summary tiles count confirmed attending guests only for headcount and wheelchair users, as the catering report will.
- "Re-enable messages" (organiser only) clears the opt-out. A finished RSVP goes back to Confirmed/Declined; an unfinished one goes back to "not invited" so it can be invited again. Answers already given are kept.
- Phone numbers are normalised to E.164. A number starting with 0 is read as South African (+27).
- Events can be created and edited. New fields: reminder interval (used in step 6). Party size is limited to 10, the most a WhatsApp list can show (still to be confirmed, D-106). It can't be lowered below a party size a guest has already given.

## Step 5: Reports and CSV

### D-401 What the report counts
Only guests whose status is Attending (they finished and said yes). Guests still answering are left out, and the page says how many, because their answers can still change. Shares are of people, not parties. Every dietary option is listed, including zeros, so the kitchen sees a complete list.

### D-402 CSV format
- UTF-8 with a BOM, CRLF line endings, RFC 4180 quoting (cells with a comma, quote, CR or LF are quoted; quotes are doubled).
- Formula injection: a cell starting with `=`, `+`, `-`, `@`, tab or CR gets a leading `'`. Only the phone column is exempt, and only when it is a plain number (`+27821234567`). An `=` later in a cell is harmless and left alone.
- Rows are streamed from the database to the response; nothing builds the whole file in memory.
- Guest list columns: Name, Phone, Status (with "(opted out)"), Party size, Dietary requirements (`1: Vegetarian; 2: Other (nut allergy)`), Wheelchair users, Access note, Last activity (event's local time).
- Catering summary: one row per requirement with the number of people, details for Other as `note (guest name, guest n)`, and a total row.
- Files are named after the event, e.g. `year-end-function-2026-guests.csv`, sent with `Cache-Control: no-store`.

### D-403 Access to reports and exports
Organisers only (D-304). Viewing the report and each export are written to the audit log.

## Open questions

1. D-004: is one migration set per provider acceptable?
2. D-104: are the behaviour choices above what you want, especially skipping the wheelchair count for a party of one and accepting "2" as the second dietary option?
3. D-304: is hiding dietary and access answers from Viewers what you want?
4. Section 7 says template approval takes time and should be submitted first. Drafting `docs/templates.md` needs the current Meta documentation, which I plan to read at the start of step 7 unless you want the template drafted sooner so you can submit it early.
