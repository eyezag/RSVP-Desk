// Guest simulator: the browser plays the guest's phone.
(() => {
  "use strict";

  const BUTTON_LIMIT = 3; // provisional WhatsApp reply-button limit, see docs/decisions.md D-106
  const POLL_MS = 1500;

  const els = {
    select: document.getElementById("guest-select"),
    status: document.getElementById("guest-status"),
    step: document.getElementById("guest-step"),
    invite: document.getElementById("invite-btn"),
    reset: document.getElementById("reset-btn"),
    notice: document.getElementById("sim-notice"),
    chat: document.getElementById("chat"),
    subtitle: document.getElementById("phone-subtitle"),
    composer: document.getElementById("composer"),
    input: document.getElementById("composer-input"),
    answers: document.getElementById("answers"),
    sheet: document.getElementById("list-sheet"),
    sheetOptions: document.getElementById("list-sheet-options"),
    sheetCancel: document.getElementById("list-sheet-cancel"),
  };

  let guestId = null;
  let renderedKey = null;
  let pollTimer = null;
  let busy = false;

  const LABELS = { NotInvited: "Not invited", InChat: "In chat", WheelchairCount: "Wheelchair count", DietOther: "Dietary (other)", OptedOut: "Opted out" };
  const label = (v) => LABELS[v] ?? v;

  const api = async (url, options = {}) => {
    const res = await fetch(url, { headers: { "Content-Type": "application/json" }, ...options });
    const body = res.status === 204 ? null : await res.json().catch(() => null);
    return { ok: res.ok, status: res.status, body };
  };

  const notice = (text) => { els.notice.textContent = text || ""; };

  const time = (iso) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  async function loadGuests() {
    const { body } = await api("/api/simulator/guests");
    els.select.innerHTML = "";
    for (const g of body ?? []) {
      const opt = document.createElement("option");
      opt.value = g.id;
      opt.textContent = g.hasConsent ? g.name : `${g.name} (no consent)`;
      els.select.append(opt);
    }
    const remembered = safeGet("sim.guest");
    if (remembered && [...els.select.options].some((o) => o.value === remembered)) els.select.value = remembered;
    selectGuest(els.select.value);
  }

  function selectGuest(id) {
    guestId = id;
    safeSet("sim.guest", id);
    renderedKey = null;
    els.chat.innerHTML = "";
    notice("");
    refresh();
  }

  async function refresh() {
    clearTimeout(pollTimer);
    if (!guestId) return;
    const { ok, body } = await api(`/api/guests/${guestId}`);
    if (ok && body) render(body);
    pollTimer = setTimeout(refresh, POLL_MS);
  }

  function render(guest) {
    els.status.textContent = label(guest.status) + (guest.optedOutAt ? " · opted out" : "") + (guest.needsAttention ? " · needs attention" : "");
    els.step.textContent = label(guest.step);
    els.subtitle.textContent = `Chatting as ${guest.name}`;
    renderAnswers(guest.answers);

    const key = guest.messages.map((m) => `${m.id}:${m.deliveryStatus ?? ""}`).join(",");
    if (key === renderedKey) return;
    renderedKey = key;

    els.chat.innerHTML = "";
    if (guest.messages.length === 0) {
      const empty = document.createElement("p");
      empty.className = "chat-empty";
      empty.textContent = "No messages yet. Send the invitation to start.";
      els.chat.append(empty);
    }
    for (const m of guest.messages) els.chat.append(bubble(m));
    els.chat.scrollTop = els.chat.scrollHeight;
  }

  function bubble(m) {
    const wrap = document.createElement("div");
    wrap.className = `bubble-wrap ${m.direction === "In" ? "from-guest" : "from-bot"}`;
    const b = document.createElement("div");
    b.className = "bubble";
    const text = document.createElement("div");
    text.className = "bubble-text";
    text.textContent = m.body || (m.replyId ? `[${m.replyId}]` : "");
    const meta = document.createElement("div");
    meta.className = "bubble-meta";
    meta.textContent = time(m.sentAt) + (m.direction === "In" ? " ✓✓" : "");
    b.append(text, meta);
    wrap.append(b);

    if (m.choices.length > 0 && m.choices.length <= BUTTON_LIMIT) {
      const row = document.createElement("div");
      row.className = "reply-buttons";
      for (const c of m.choices) row.append(choiceButton(c, "reply-button"));
      wrap.append(row);
    } else if (m.choices.length > BUTTON_LIMIT) {
      const open = document.createElement("button");
      open.type = "button";
      open.className = "reply-button list-open";
      open.textContent = "☰ Choose";
      open.addEventListener("click", () => openSheet(m.choices));
      wrap.append(open);
    }
    return wrap;
  }

  function choiceButton(choice, cls) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = cls;
    btn.textContent = choice.title;
    btn.addEventListener("click", () => { closeSheet(); send({ text: choice.title, replyId: choice.id }); });
    return btn;
  }

  function openSheet(choices) {
    els.sheetOptions.innerHTML = "";
    for (const c of choices) els.sheetOptions.append(choiceButton(c, "list-row"));
    els.sheet.hidden = false;
    els.sheetOptions.querySelector("button")?.focus();
  }

  function closeSheet() { els.sheet.hidden = true; }

  function renderAnswers(a) {
    const rows = [
      ["Attending", a.attending === null ? "–" : a.attending ? "Yes" : "No"],
      ["Party size", a.partySize ?? "–"],
      ["Dietary", a.diets.length ? a.diets.map((d) => `${d.personIndex}: ${d.dietary}${d.note ? ` (${d.note})` : ""}`).join("\n") : "–"],
      ["Wheelchair users", a.wheelchairUsers ?? "–"],
      ["Access note", a.accessNote ?? "–"],
      ["Completed", a.confirmedAt ? new Date(a.confirmedAt).toLocaleString() : "–"],
    ];
    els.answers.innerHTML = "";
    for (const [k, v] of rows) {
      const dt = document.createElement("dt");
      dt.textContent = k;
      const dd = document.createElement("dd");
      dd.textContent = String(v);
      els.answers.append(dt, dd);
    }
  }

  async function send(payload) {
    if (!guestId || busy) return;
    busy = true;
    try {
      const { ok, body } = await api(`/api/simulator/${guestId}/messages?wait=true`, { method: "POST", body: JSON.stringify(payload) });
      if (!ok) notice(body?.errors ? Object.values(body.errors).flat().join(" ") : "Could not send.");
      else notice("");
    } finally {
      busy = false;
      refresh();
    }
  }

  els.composer.addEventListener("submit", (e) => {
    e.preventDefault();
    const text = els.input.value.trim();
    if (!text) return;
    els.input.value = "";
    send({ text });
  });

  els.invite.addEventListener("click", async () => {
    const { ok, body } = await api(`/api/simulator/${guestId}/invite`, { method: "POST" });
    const reasons = { OptedOut: "This guest opted out. Reset them to try again.", NoConsent: "No consent is recorded for this guest, so they can't be messaged.", AlreadyInConversation: "This guest is already in a conversation. Reset them first." };
    notice(ok ? "" : reasons[body?.outcome] ?? "Could not send the invitation.");
    refresh();
  });

  els.reset.addEventListener("click", async () => {
    await api(`/api/simulator/${guestId}/reset`, { method: "POST" });
    renderedKey = null;
    notice("Guest reset.");
    refresh();
  });

  els.select.addEventListener("change", () => selectGuest(els.select.value));
  els.sheetCancel.addEventListener("click", closeSheet);
  els.sheet.addEventListener("click", (e) => { if (e.target === els.sheet) closeSheet(); });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeSheet(); });

  function safeGet(k) { try { return localStorage.getItem(k); } catch { return null; } }
  function safeSet(k, v) { try { localStorage.setItem(k, v); } catch { /* ignore */ } }

  loadGuests();
})();
