# WhatsApp webhook fixtures

Anonymised samples shaped exactly like the payloads in Meta's documentation (see `docs/whatsapp-integration.md` §3–4). No real data: the phone number `27000000001` is in an unallocated range, the business-scoped user id and all message ids are made up, and the profile name is fictional.

| File | What it is |
|---|---|
| `text.json` | Guest types a text message |
| `button_reply.json` | Guest taps an interactive reply button |
| `list_reply.json` | Guest picks a row from an interactive list |
| `template_quick_reply.json` | Guest taps a template quick-reply button (`type: "button"`) |
| `username_only.json` | Text from a user with a username: no `from` or `wa_id`, only the BSUID |
| `reaction.json` | Guest reacts to a message (ignored) |
| `status_sent.json`, `status_delivered.json`, `status_read.json` | Delivery statuses |
| `status_failed.json` | Failed delivery with an error |
| `batch.json` | Two messages and a status in one POST |
