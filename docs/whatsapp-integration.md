# WhatsApp Cloud API: what the documentation says

Read on 11 November 2026 from Meta's developer documentation (pages "Updated" between April and November 2026). Meta has moved most WhatsApp pages from `developers.facebook.com/docs/whatsapp/...` to `developers.facebook.com/documentation/business-messaging/whatsapp/...`; both forms are given where they differ. Anything marked **not confirmed** was not stated on the pages I read and is a design assumption, not a fact.

## 1. Webhook verification (GET)

Source: `documentation/business-messaging/whatsapp/webhooks/create-webhook-endpoint/`, `docs/graph-api/webhooks/getting-started`.

- Meta sends `GET <callback>?hub.mode=subscribe&hub.challenge=<random>&hub.verify_token=<your string>` whenever the callback URL or verify token is set or edited in the App Dashboard.
- `hub.mode` is always `subscribe`. Compare `hub.verify_token` with the string stored on the server.
- Valid: respond **200** with the `hub.challenge` value as the body. Invalid: any 4xx (anything but 200). Until it gets 200 + challenge, Meta sends no webhooks.

Implementation: `GET /webhooks/whatsapp` compares the token in constant time and returns the challenge as `text/plain`; otherwise 403.

## 2. Signed event notifications (POST)

Same sources.

- Header: `X-Hub-Signature-256: sha256=<hex>`.
- Value: HMAC-SHA256 of the **raw POST body**, keyed with the **app secret**. Compare with everything after `sha256=`. Mismatch → treat as invalid, respond 4xx.
- Valid requests should get **200**. Payloads can be up to **3 MB**. Batches can hold up to 1000 updates, but batching isn't guaranteed.
- Failed deliveries are retried immediately, then with decreasing frequency (exponential back-off) for up to **7 days** (the older generic Graph page says 36 hours; the WhatsApp pages say 7 days). Retries go to every subscribed app and **can produce duplicates**, so the receiver must deduplicate. There is no API to fetch past webhooks.
- mTLS is available as an extra layer (not used here).

Implementation: the raw body is read once (limit 3 MB), the HMAC is checked with `CryptographicOperations.FixedTimeEquals` before anything is parsed or stored, and bad or missing signatures get 401. Valid payloads are split into one stored `WebhookEvent` per message or status (unique `ExternalId`), queued, and answered with 200 at once; processing happens in the background worker.

## 3. Inbound message payloads

Sources: `webhooks/overview/`, `webhooks/reference/messages/text|interactive|button/`, `messages/interactive-reply-buttons-messages/`, `messages/interactive-list-messages/`.

Envelope: `object = "whatsapp_business_account"`, `entry[].changes[]` with `field = "messages"` and a `value` holding `messaging_product`, `metadata { display_phone_number, phone_number_id }`, `contacts[] { profile { name, username? }, wa_id?, user_id }` and `messages[]`.

Each message has `id` (`wamid....`), `timestamp` (Unix seconds, string), `type`, `from` and `from_user_id`, and:

| Type | Field | Used as |
|---|---|---|
| `text` | `text.body` | typed text |
| `interactive` / `button_reply` | `interactive.button_reply { id, title }` | reply id + title |
| `interactive` / `list_reply` | `interactive.list_reply { id, title, description }` | reply id + title |
| `button` (template quick reply) | `button { payload, text }` | text (and a configured label → id map, see §6) |

Anonymised samples: `tests/fixtures/whatsapp/`.

**Users and phone numbers (new in 2026):** every webhook carries a business-scoped user ID (BSUID, e.g. `US.13491208655302741918`) in `contacts[].user_id` / `messages[].from_user_id`. If the user has adopted a WhatsApp username and their phone number is unavailable, `wa_id` and `from` are **omitted**, and Meta says an integration that relies only on the phone number won't be able to process those messages. In the examples `from` is digits without `+` (`16505551234`), though one description shows `+16505551234`.

Implementation: `from` is normalised to `+digits`. The guest is found by phone number; the BSUID is then stored on the guest, so later messages that carry only a BSUID still find them. A message with neither a known phone nor a known BSUID is recorded as an error against the event.

Other types (image, audio, location, sticker, ...) reach the engine as a message without text, so the bot repeats its question. Reactions and system messages are ignored.

## 4. Delivery status callbacks

Source: `webhooks/reference/messages/status`.

- `value.statuses[] { id, status, timestamp, recipient_id?, recipient_user_id, errors[]? }`, where `id` is the `wamid` returned when we sent the message.
- `status`: `sent`, `delivered`, `read`, `failed`, `played` (voice messages).
- "delivered" is **not always sent**: a message delivered and read at once only gets "read". Order of delivery is not stated as guaranteed, and retries can repeat a status.
- `errors[] { code, title, message, error_data { details } }` on failures.

Implementation: one `WebhookEvent` per (message id, status), so repeats are ignored. Statuses only move forward (Pending → Sent → Delivered → Read; `played` counts as Read). `failed` is applied only while the message is not yet Delivered/Read, and its code and title are stored in `Messages.DeliveryError`. A status for a message id we don't know is recorded as an error on the event.

## 5. Sending messages

Sources: `messages/interactive-reply-buttons-messages/`, `messages/interactive-list-messages/`, `messages/text-messages/`, `templates/overview/`.

- `POST https://graph.facebook.com/<API_VERSION>/<PHONE_NUMBER_ID>/messages`, headers `Authorization: Bearer <token>`, `Content-Type: application/json`. Examples use **v27.0** (configurable).
- Body: `{ "messaging_product": "whatsapp", "recipient_type": "individual", "to": "+16505551234", "type": ..., <type>: {...} }`. `to` is shown with a `+`. A BSUID can be used with `recipient` instead of `to`.
- Response: `{ "messaging_product": "whatsapp", "contacts": [{ "input", "wa_id" }], "messages": [{ "id": "wamid..." }] }`.
- Errors: `{ "error": { "message", "type", "code", "error_data": { "messaging_product", "details" }, "error_subcode", "fbtrace_id } }`. Meta recommends handling by `code` and `details`, not titles.
- Template send: `type: "template"`, `template { name, language { code }, components: [{ type: "body", parameters: [{ type: "text", parameter_name: "event_name", text: "..." }] }] }` for named parameters.

## 6. Limits that shape the conversation

| Item | Limit | Source |
|---|---|---|
| Reply buttons per message | 3 | interactive reply buttons |
| Reply button title | 20 characters, unique within the message | same |
| Reply button id | 256 characters | same |
| Reply-button message body | 1024 characters | same |
| List rows | 10 in total across all sections | interactive list |
| List sections | up to 10; each needs a title (24 characters) | same |
| List row title / id / description | 24 / 200 / 72 characters | same |
| List button text | 20 characters | same |
| List message body | 4096 characters | same |
| Template body | 1024 characters | templates/components |
| Template quick-reply buttons | up to 10; label 25 characters (marketing example says "alphanumeric characters only") | templates/components, custom marketing templates |
| Template names | lowercase letters, digits and underscores, up to 512 characters | templates/overview |

How the RSVP fits: yes/no questions and the Skip and Change options use reply buttons (2 or 1 options). Party size (1 to max) and wheelchair count use buttons when there are 3 or fewer options and a list otherwise; the event's maximum party size is capped at **10** to fit one list. The six dietary options use a list. Labels are at most 20 characters, which fits both buttons and list rows; a test enforces it. Typed answers still work everywhere.

## 7. Business-initiated messages and the 24-hour window

Sources: `templates/overview/`, `pricing/non-template-messages/`, `messages/send-messages/`, error codes.

- A **customer service window** of 24 hours opens when the user messages (or calls) the business and resets with each user message. Non-template ("service") messages can only be sent inside it.
- Outside it, only **template** messages can be sent. Templates must be approved (`APPROVED`) before use; review "can take up to 24 hours".
- Sending a free-form message outside the window fails with **131047** ("More than 24 hours have passed since the recipient last replied... Send the recipient a template message instead").
- Pricing note (affects cost, not code): from 1 October 2026 Meta charges per message for utility messages sent within an open window.

Implementation: invitations are always templates. Reminders are templates unless the guest has written in the last 24 hours. Replies are ordinary messages, since they always answer the guest. Templates are in `docs/templates.md`.

## 8. Template categories and opt-in

Sources: `templates/template-categorization/`, `getting-opt-in/`.

- Categories: marketing, utility, authentication. Utility must be non-promotional **and** specific to or requested by the user (their order, account, services or transactions) or critical to them. Meta may approve a template submitted as utility as **marketing** instead (default since April 2025), and an event-promotion example appears under marketing.
- My judgement, not a documented rule: an RSVP invitation sent to someone who registered or asked to be invited, worded without promotion, fits utility best. Meta decides; plan for it being categorised as marketing.
- Opt-in is required before messaging. It can be general (not WhatsApp-specific) if local law allows, must say the person is opting in to messages, and must name the business. Methods include web, SMS, phone and paper. RSVP Desk stores `ConsentAt` and `ConsentSource` and never messages guests without them.

## 9. Test number and development

Source: `get-started/`.

- Create a Meta app with the "Connect with customers through WhatsApp" use case, connect or create a WhatsApp Business (Messaging) account, then in **API Setup** generate a temporary token, pick a "From" test number and add a "To" number.
- The temporary token expires quickly; create a **system user** with `business_management` and `whatsapp_business_messaging` permissions and generate a permanent token.
- Configure the webhook URL and verify token in App Dashboard > WhatsApp > Configuration (or Use cases > Customize > Configuration) and subscribe to the `messages` field.
- Some webhooks aren't sent while the app is in Dev mode.
- **Not confirmed:** how many recipient numbers a test number can message, and the exact rules for recipients while in development. The page I read doesn't state them; check the API Setup panel.

## 10. Rate limits and retries

Sources: `throughput/`, error codes.

- Throughput: up to **80 messages per second** per business number by default, automatically upgraded to 1000 for eligible numbers. Over the limit → **130429**.
- Too many messages to the **same** user in a short time → **131056** (pair rate limit); wait before retrying that user, other users are unaffected.
- **80007**: account rate limit; **4**: app API call rate limit; **131000**: unknown error, try again; **131016**: service temporarily unavailable.
- Not retryable: **131047** (outside the window: use a template), **131026** (undeliverable: not on WhatsApp, old version, terms not accepted), **132000/132001** (template parameter mismatch / template missing or not approved), **131008/131009** (bad request), **190** (expired token).
- Your webhook should handle about 3× the outgoing message rate in status callbacks.

Implementation: up to 3 attempts with exponential back-off (1 s, 2 s, 4 s by default; `Retry-After` is honoured) for HTTP 429/5xx, network errors and codes 4, 80007, 130429, 131000, 131016, 131056. Everything else fails at once, and the message is marked Failed with the code and title. A bulk send can add a pause between messages (`Sending:DelayBetweenMessagesMs`), though at RSVP volumes the 80 mps limit is far away.
