import { validateReport } from "./validate";

const MAX_BODY_BYTES = 8 * 1024;
const COUNTS_TTL_SECONDS = 600;
const DAY_MS = 24 * 60 * 60 * 1000;
const RETENTION_DAYS = 180;
// Reports for a quest path are dropped this long after a newer path for the same quest first appeared.
const REPLACED_PATH_DAYS = 30;
const ADMIN_REPORT_LIMIT = 200;

// Stable internal URL: the Cache API keys on requests, and workers.dev has no zone cache of its own.
const COUNTS_CACHE_KEY = "https://qst-reports.internal/v1/counts";
let memoryCounts: { body: string; expiresAt: number } | null = null;

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

async function rateLimited(limiter: RateLimit | undefined, request: Request): Promise<Response | null> {
  if (!limiter) {
    return null;
  }

  const key = request.headers.get("CF-Connecting-IP") ?? "local";
  const { success } = await limiter.limit({ key });
  return success ? null : json({ error: "too many requests" }, 429, { "Retry-After": "60" });
}

async function readBody(request: Request): Promise<string | null> {
  const declared = request.headers.get("Content-Length");
  if (declared !== null && Number(declared) > MAX_BODY_BYTES) {
    return null;
  }

  const bytes = await request.arrayBuffer();
  return bytes.byteLength > MAX_BODY_BYTES ? null : new TextDecoder().decode(bytes);
}

async function postReport(request: Request, env: Env): Promise<Response> {
  if (!env.CHARACTER_SALT) {
    return json({ error: "not configured" }, 503);
  }

  const limited = await rateLimited(env.REPORT_IP_LIMITER, request);
  if (limited) {
    return limited;
  }

  const text = await readBody(request);
  if (text === null) {
    return json({ error: "too large" }, 413);
  }

  let body: unknown;
  try {
    body = JSON.parse(text);
  } catch {
    return json({ error: "invalid json" }, 400);
  }

  const result = validateReport(body);
  if (!result.ok) {
    return json({ error: result.error }, 400);
  }

  const report = result.report;
  // The client already sends a hash; salting it again here means stored values can't be matched to characters.
  const character = await sha256Hex(`${env.CHARACTER_SALT}:${report.character}`);
  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare("INSERT INTO paths (quest_id, path_hash, first_seen) VALUES (?, ?, ?) ON CONFLICT DO NOTHING")
      .bind(report.quest, report.path, now),
    env.DB.prepare(`
      INSERT INTO reports (character, quest_id, path_hash, worked, kind, sequence, step, versions, actions, created_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT (character, quest_id, path_hash) DO UPDATE SET
        worked = excluded.worked, kind = excluded.kind, sequence = excluded.sequence, step = excluded.step,
        versions = excluded.versions, actions = excluded.actions, created_at = excluded.created_at`)
      .bind(character, report.quest, report.path, report.worked ? 1 : 0, report.kind, report.sequence, report.step,
        report.versions ? JSON.stringify(report.versions) : null,
        report.actions ? JSON.stringify(report.actions) : null, now),
  ]);

  return json({ ok: true });
}

function countsResponse(body: string): Response {
  return new Response(body, {
    headers: { "Content-Type": "application/json", "Cache-Control": `public, max-age=${COUNTS_TTL_SECONDS}` },
  });
}

async function getCounts(env: Env, ctx: ExecutionContext): Promise<Response> {
  if (memoryCounts && memoryCounts.expiresAt > Date.now()) {
    return countsResponse(memoryCounts.body);
  }

  const cached = await caches.default.match(COUNTS_CACHE_KEY);
  if (cached) {
    return cached;
  }

  const { results } = await env.DB.prepare(`
    SELECT quest_id, path_hash, SUM(worked) AS worked, COUNT(*) AS total
    FROM reports GROUP BY quest_id, path_hash`)
    .all<{ quest_id: string; path_hash: string; worked: number; total: number }>();

  // { quests: { "1190": { "<path>": [worked, total] } } }; the client picks the path it runs.
  const quests: Record<string, Record<string, [number, number]>> = {};
  for (const row of results) {
    (quests[row.quest_id] ??= {})[row.path_hash] = [row.worked, row.total];
  }

  const body = JSON.stringify({ quests });
  memoryCounts = { body, expiresAt: Date.now() + COUNTS_TTL_SECONDS * 1000 };
  ctx.waitUntil(caches.default.put(COUNTS_CACHE_KEY, countsResponse(body)));
  return countsResponse(body);
}

// Tests post reports and read counts in the same isolate.
export async function resetCountsCache(): Promise<void> {
  memoryCounts = null;
  await caches.default.delete(COUNTS_CACHE_KEY);
}

async function findAdmin(request: Request, env: Env): Promise<string | null> {
  const header = request.headers.get("Authorization");
  if (!header?.startsWith("Bearer ")) {
    return null;
  }

  const tokenHash = await sha256Hex(header.slice("Bearer ".length).trim());
  const admin = await env.DB.prepare("SELECT name FROM admins WHERE token_hash = ?")
    .bind(tokenHash)
    .first<{ name: string }>();
  return admin?.name ?? null;
}

async function getAdminReports(request: Request, env: Env, url: URL): Promise<Response> {
  const limited = await rateLimited(env.ADMIN_IP_LIMITER, request);
  if (limited) {
    return limited;
  }

  const admin = await findAdmin(request, env);
  if (!admin) {
    return json({ error: "unauthorized" }, 401);
  }

  const quest = url.searchParams.get("quest");
  if (!quest || quest.length > 16) {
    return json({ error: "quest" }, 400);
  }

  console.log(`admin ${admin} read reports for quest ${quest}`);
  const { results } = await env.DB.prepare(`
    SELECT path_hash, kind, sequence, step, versions, actions, created_at
    FROM reports WHERE quest_id = ? AND worked = 0
    ORDER BY created_at DESC LIMIT ?`)
    .bind(quest, ADMIN_REPORT_LIMIT)
    .all<{
      path_hash: string; kind: string; sequence: number | null; step: number | null;
      versions: string | null; actions: string | null; created_at: number;
    }>();

  return json({
    reports: results.map(row => ({
      path: row.path_hash,
      kind: row.kind,
      sequence: row.sequence,
      step: row.step,
      versions: row.versions ? JSON.parse(row.versions) : null,
      actions: row.actions ? JSON.parse(row.actions) : null,
      at: row.created_at,
    })),
  });
}

export async function prune(env: Env, now: number): Promise<void> {
  const replacedBefore = now - REPLACED_PATH_DAYS * DAY_MS;
  await env.DB.batch([
    env.DB.prepare("DELETE FROM reports WHERE created_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    // A path counts as replaced once a newer path for the same quest has been around long enough.
    env.DB.prepare(`
      DELETE FROM reports WHERE EXISTS (
        SELECT 1 FROM paths old JOIN paths newer
          ON newer.quest_id = old.quest_id AND newer.first_seen > old.first_seen
        WHERE old.quest_id = reports.quest_id AND old.path_hash = reports.path_hash
          AND newer.first_seen < ?)`)
      .bind(replacedBefore),
    env.DB.prepare("DELETE FROM paths WHERE NOT EXISTS (SELECT 1 FROM reports WHERE reports.quest_id = paths.quest_id AND reports.path_hash = paths.path_hash) AND first_seen < ?")
      .bind(now - RETENTION_DAYS * DAY_MS),
  ]);
}

export default {
  async fetch(request, env, ctx): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/v1/reports" && request.method === "POST") {
        return await postReport(request, env);
      }

      if (url.pathname === "/v1/counts" && request.method === "GET") {
        return await getCounts(env, ctx);
      }

      if (url.pathname === "/v1/admin/reports" && request.method === "GET") {
        return await getAdminReports(request, env, url);
      }

      return json({ error: "not found" }, 404);
    } catch (error) {
      console.error(error);
      return json({ error: "internal error" }, 500);
    }
  },

  async scheduled(controller, env): Promise<void> {
    await prune(env, controller.scheduledTime);
  },
} satisfies ExportedHandler<Env>;
