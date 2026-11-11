# WhatsApp message templates

Invitations and reminders reach guests outside the 24-hour customer service window, so they must be approved templates (`docs/whatsapp-integration.md` §7). Submit these first: review can take up to 24 hours. Create them in WhatsApp Manager (Manage templates) or with the Message Templates API.

All three use **named parameters**, language **English (`en`)**, and category **Utility** (requested; Meta may approve them as Marketing, see §8 of the integration notes). Wording is non-promotional and says why the information is collected, as POPIA requires. Quick-reply labels are plain words so they pass the "alphanumeric" rule seen in Meta's examples.

The app maps tapped quick-reply labels to conversation options with `WhatsApp:QuickReplies` (below), so it doesn't rely on custom button payloads.

## 1. `rsvp_invitation`

Body (under 1024 characters):

```
Hello! You're invited to {{event_name}} on {{event_date}} at {{venue}}.

Please let us know if you can come. If you can, we'll ask how many people are coming and about any dietary or access needs, so we can plan catering and access. Your answers are only used for this event.

Reply STOP at any time to stop messages.
```

Example values: `event_name` = "Year-End Function 2026", `event_date` = "Saturday 5 December 2026 at 18:00", `venue` = "The Jacaranda Hall, Johannesburg".

Buttons (quick reply): `Yes` · `No`

## 2. `rsvp_reminder`

For guests who haven't replied.

```
Just a reminder: we'd love to know if you can come to {{event_name}} on {{event_date}}.

Reply STOP if you'd rather not hear from us.
```

Buttons (quick reply): `Yes` · `No`

## 3. `rsvp_reminder_unfinished`

For guests who started answering but stopped.

```
Just a reminder to finish your RSVP for {{event_name}} on {{event_date}}. Tap Continue to pick up where you left off.

Reply STOP if you'd rather not hear from us.
```

Buttons (quick reply): `Continue`

## Configuration

```json
"WhatsApp": {
  "TemplateLanguage": "en",
  "Templates": {
    "Invitation": "rsvp_invitation",
    "ReminderNotReplied": "rsvp_reminder",
    "ReminderUnfinished": "rsvp_reminder_unfinished"
  },
  "QuickReplies": {
    "Yes": "attend.yes",
    "No": "attend.no",
    "Continue": "resume"
  }
}
```

Tapping **Yes** or **No** answers the attend question. **Continue** makes the bot repeat the guest's current question. Typed replies work too.
