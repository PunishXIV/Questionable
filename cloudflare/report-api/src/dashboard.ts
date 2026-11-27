// Same rules as ReportStatus.Evaluate in the plugin.
export const DASHBOARD = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>QST Path Status</title>
<style>
  :root { color-scheme: light dark; --bg: #f6f6f4; --card: #fff; --text: #1d1d1b; --muted: #6b6b66; --line: #e2e2dc;
    --red: #dc2626; --amber: #b45309; --green: #15803d; }
  @media (prefers-color-scheme: dark) { :root { --bg: #161615; --card: #20201e; --text: #ecece8; --muted: #9a9a93;
    --line: #34342f; --red: #f87171; --amber: #fbbf24; --green: #4ade80; } }
  * { box-sizing: border-box; }
  [hidden] { display: none !important; }
  body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.45 system-ui, sans-serif; }
  main { max-width: 980px; margin: 0 auto; padding: 24px 16px 48px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  .muted { color: var(--muted); }
  .bar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 16px 0; }
  .chip { border: 1px solid var(--line); background: var(--card); border-radius: 999px; padding: 4px 12px; cursor: pointer;
    color: var(--text); font: inherit; }
  .chip[aria-pressed="true"] { border-color: currentColor; }
  .chip b { font-variant-numeric: tabular-nums; }
  .red { color: var(--red); } .amber { color: var(--amber); } .green { color: var(--green); }
  input { flex: 1 1 200px; min-width: 0; padding: 6px 10px; border: 1px solid var(--line); border-radius: 6px;
    background: var(--card); color: var(--text); font: inherit; }
  section { background: var(--card); border: 1px solid var(--line); border-radius: 8px; margin-top: 16px; overflow-x: auto; }
  h2 { font-size: 15px; margin: 0; padding: 12px 14px; border-bottom: 1px solid var(--line); }
  table { width: 100%; border-collapse: collapse; }
  th, td { text-align: left; padding: 7px 14px; border-bottom: 1px solid var(--line); white-space: nowrap; }
  th { font-weight: 600; color: var(--muted); font-size: 12px; }
  td.name { white-space: normal; }
  td.num { font-variant-numeric: tabular-nums; }
  tr:last-child td { border-bottom: 0; }
  .empty { padding: 12px 14px; }
  .pad { padding: 12px 14px; margin: 0; }
  button.chip:disabled { opacity: .5; cursor: default; }
</style>
</head>
<body>
<main>
  <h1>Questionable path status</h1>
  <div class="muted" id="summary">Loading…</div>
  <div class="bar">
    <button class="chip red" data-show="problems" aria-pressed="true">Has issues <b id="n-problems">0</b></button>
    <button class="chip amber" data-show="review" aria-pressed="true">Needs verification <b id="n-review">0</b></button>
    <button class="chip green" data-show="working" aria-pressed="true">Verified <b id="n-working">0</b></button>
    <input id="search" type="search" placeholder="Search quest name or ID" aria-label="Search quests">
  </div>
  <div id="lists"></div>
  <section id="admin">
    <h2>Uploaded troubleshooting zips</h2>
    <form class="bar pad" id="admin-login">
      <input id="token" type="password" placeholder="Admin or viewer token" autocomplete="off" aria-label="Admin or viewer token">
      <button class="chip" type="submit">Sign in</button>
    </form>
    <div id="admin-body" hidden>
      <form class="bar pad" id="zip-filter">
        <input id="zip-quest" type="search" placeholder="Quest ID" aria-label="Quest ID">
        <input id="zip-code" type="search" placeholder="Report code" aria-label="Report code">
        <button class="chip" type="submit">Show</button>
        <button class="chip" type="button" id="zip-signout">Sign out</button>
      </form>
      <div id="zip-list" class="empty muted">Loading…</div>
    </div>
  </section>
</main>
<script>
const DAY = 86400000, RECENT_DAYS = 30;
const GROUPS = [
  { key: "problems", title: "Has issues", cls: "red", note: "Unresolved problem reported in the last 30 days" },
  { key: "review", title: "Needs verification", cls: "amber", note: "Fewer than 5 recent successful runs and no recent verification" },
  { key: "working", title: "Verified", cls: "green", note: "Verified by an admin or 5 players' runs worked in the last 30 days" },
];
const shown = new Set(GROUPS.map(g => g.key));
let rows = [], names = {};

function evaluate(s, cutoff) {
  const recent = at => at != null && at >= cutoff;
  if (s.lastReportAt == null && s.verifiedAt == null) return null;
  if (recent(s.lastProblemAt)) return ["problems", "Problems"];
  if (recent(s.verifiedAt)) return ["working", "Verified by admin"];
  if (recent(s.fifthWorkedAt)) return ["working", "5 players"];
  return ["review", recent(s.lastReportAt) ? "Few reports" : "Old reports"];
}

function ago(at) {
  if (at == null) return "–";
  const days = Math.floor((Date.now() - at) / DAY);
  return days < 1 ? "today" : days === 1 ? "1 day ago" : days + " days ago";
}

function cell(tr, text, cls) {
  const td = tr.insertCell();
  td.textContent = text;
  if (cls) td.className = cls;
}

function render() {
  const query = document.getElementById("search").value.trim().toLowerCase();
  const lists = document.getElementById("lists");
  lists.replaceChildren();
  for (const group of GROUPS) {
    const items = rows.filter(r => r.group === group.key);
    document.getElementById("n-" + group.key).textContent = items.length;
    if (!shown.has(group.key)) continue;
    const visible = items.filter(r => !query || r.quest.toLowerCase().includes(query) ||
      (names[r.quest] || "").toLowerCase().includes(query));
    const section = document.createElement("section");
    const h2 = document.createElement("h2");
    h2.className = group.cls;
    h2.textContent = group.title + " (" + visible.length + ")";
    h2.title = group.note;
    section.append(h2);
    if (visible.length === 0) {
      const empty = document.createElement("div");
      empty.className = "empty muted";
      empty.textContent = "None";
      section.append(empty);
    } else {
      const table = document.createElement("table");
      const head = table.createTHead().insertRow();
      const problems = group.key === "problems";
      const labels = ["Quest", "ID", "Why", "Worked / total", "Recent problems", "Last report"];
      if (problems) labels.push("QST version");
      for (const label of labels) {
        const th = document.createElement("th");
        th.textContent = label;
        head.append(th);
      }
      const body = table.createTBody();
      for (const r of visible) {
        const tr = body.insertRow();
        cell(tr, names[r.quest] || "–", "name");
        cell(tr, r.quest, "num");
        cell(tr, r.reason);
        cell(tr, r.s.worked + " / " + r.s.total, "num");
        cell(tr, String(r.s.recentProblems), "num");
        cell(tr, ago(r.s.lastReportAt));
        if (problems) cell(tr, r.s.lastProblemVersion || "–", "num");
      }
      section.append(table);
    }
    lists.append(section);
  }
}

async function loadNames(quests, after = render) {
  const ids = [...new Set(quests)].filter(q => /^\\d+$/.test(q) && !(q in names)).map(q => 65536 + Number(q));
  for (let i = 0; i < ids.length; i += 200) {
    try {
      const reply = await fetch("https://v2.xivapi.com/api/sheet/Quest?limit=500&fields=Name&rows=" + ids.slice(i, i + 200).join(","));
      for (const row of (await reply.json()).rows) names[String(row.row_id - 65536)] = row.fields.Name;
    } catch { return; }
    after();
  }
}

// Admin box: the token stays in this tab only.
let token = null, zips = [];
try { token = sessionStorage.getItem("qst-token"); } catch {}

function remember(value) {
  token = value;
  try { value ? sessionStorage.setItem("qst-token", value) : sessionStorage.removeItem("qst-token"); } catch {}
  document.getElementById("admin-login").hidden = !!value;
  document.getElementById("admin-body").hidden = !value;
}

function renderZips() {
  const list = document.getElementById("zip-list");
  if (zips.length === 0) {
    list.className = "empty muted";
    list.textContent = "No uploaded zips.";
    return;
  }
  const table = document.createElement("table");
  const head = table.createTHead().insertRow();
  for (const label of ["Code", "Quest", "ID", "Problem", "QST version", "Size", "Uploaded", ""]) {
    const th = document.createElement("th");
    th.textContent = label;
    head.append(th);
  }
  const body = table.createTBody();
  for (const z of zips) {
    const tr = body.insertRow();
    cell(tr, z.code, "num");
    cell(tr, z.quest_id ? names[z.quest_id] || "–" : "No quest", "name");
    cell(tr, z.quest_id || "–", "num");
    cell(tr, z.kind || "–");
    cell(tr, z.version || "–", "num");
    cell(tr, Math.max(1, Math.round(z.size / 1024)) + " KB", "num");
    cell(tr, new Date(z.created_at).toLocaleString());
    const button = document.createElement("button");
    button.className = "chip";
    button.type = "button";
    button.textContent = "Download";
    button.addEventListener("click", () => download(z.code, button));
    tr.insertCell().append(button);
  }
  list.className = "";
  list.replaceChildren(table);
}

async function adminFetch(path) {
  const reply = await fetch(path, { headers: { Authorization: "Bearer " + token } });
  if (reply.status === 401) {
    remember(null);
    document.getElementById("token").placeholder = "That token was not accepted";
    throw new Error("unauthorized");
  }
  if (!reply.ok) throw new Error(String(reply.status));
  return reply;
}

async function loadZips() {
  const list = document.getElementById("zip-list");
  const quest = document.getElementById("zip-quest").value.trim();
  const code = document.getElementById("zip-code").value.trim().toUpperCase();
  const query = code ? "?code=" + encodeURIComponent(code) : quest ? "?quest=" + encodeURIComponent(quest) : "";
  list.className = "empty muted";
  list.textContent = "Loading…";
  try {
    zips = (await (await adminFetch("/v2/admin/logs" + query)).json()).zips;
    renderZips();
    loadNames(zips.map(z => z.quest_id).filter(Boolean), renderZips);
  } catch (e) {
    if (e.message !== "unauthorized") list.textContent = "Could not load the zips (" + e.message + ").";
  }
}

async function download(code, button) {
  button.disabled = true;
  try {
    const blob = await (await adminFetch("/v2/admin/logs/" + encodeURIComponent(code))).blob();
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = "QST-" + code + ".zip";
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 10000);
  } catch (e) {
    if (e.message !== "unauthorized") button.textContent = "Failed (" + e.message + ")";
  } finally {
    button.disabled = false;
  }
}

document.getElementById("admin-login").addEventListener("submit", event => {
  event.preventDefault();
  const value = document.getElementById("token").value.trim();
  document.getElementById("token").value = "";
  if (!value) return;
  remember(value);
  loadZips();
});
document.getElementById("zip-filter").addEventListener("submit", event => {
  event.preventDefault();
  loadZips();
});
document.getElementById("zip-signout").addEventListener("click", () => {
  remember(null);
  zips = [];
});
remember(token);
if (token) loadZips();

async function load() {
  const summary = document.getElementById("summary");
  try {
    const reply = await fetch("/v2/status");
    if (!reply.ok) throw new Error(String(reply.status));
    const { at, quests } = await reply.json();
    const cutoff = Date.now() - RECENT_DAYS * DAY;
    rows = [];
    for (const [quest, paths] of Object.entries(quests)) {
      const s = Object.values(paths).sort((a, b) =>
        (b.lastReportAt ?? b.verifiedAt ?? 0) - (a.lastReportAt ?? a.verifiedAt ?? 0))[0];
      const result = s && evaluate(s, cutoff);
      if (result) rows.push({ quest, s, group: result[0], reason: result[1] });
    }
    rows.sort((a, b) => (b.s.lastProblemAt ?? b.s.lastReportAt ?? 0) - (a.s.lastProblemAt ?? a.s.lastReportAt ?? 0));
    summary.textContent = rows.length + " quests with reports · data from " + new Date(at).toLocaleString() +
      " (refreshes every 10 minutes)";
    render();
    loadNames(rows.map(r => r.quest));
  } catch (e) {
    summary.textContent = "Could not load the report status (" + e.message + ").";
  }
}

for (const chip of document.querySelectorAll(".chip")) {
  chip.addEventListener("click", () => {
    const key = chip.dataset.show;
    if (shown.has(key)) shown.delete(key); else shown.add(key);
    chip.setAttribute("aria-pressed", String(shown.has(key)));
    render();
  });
}
document.getElementById("search").addEventListener("input", render);
load();
</script>
</body>
</html>
`;
