// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
import { PATH, QUEST } from "./validate";

const WINDOW_MS = 30 * 24 * 60 * 60 * 1000;
const ADMIN_REPORT_LIMIT = 200;
const STATUS_TTL_SECONDS = 600;
const STATUS_CACHE_KEY = "https://qst-reports.internal/v2/status";
let memoryStatus: { body: string; expiresAt: number } | null = null;

export interface PathStatus {
  worked: number;
  total: number;
  recentWorked: number;
  recentProblems: number;
  lastReportAt: number | null;
  lastWorkedAt: number | null;
  fifthWorkedAt: number | null;
  lastProblemAt: number | null;
  lastProblemVersion: string | null;
  verifiedAt: number | null;
}

type StatusRow = PathStatus & { quest_id: string; path_hash: string };
type EventRow = {
  id: number; path_hash: string; kind: string; sequence: number | null; step: number | null;
  versions: string | null; actions: string | null; created_at: number;
  resolved_at: number | null; resolved_by: string | null; current: number;
};

function reply(body: unknown, status = 200): Response {
  return Response.json(body, { status, headers: { "Cache-Control": "no-store" } });
}

function cachedResponse(body: string): Response {
  return new Response(body, {
    headers: { "Content-Type": "application/json", "Cache-Control": `public, max-age=${STATUS_TTL_SECONDS}` },
  });
}

export async function resetStatusCache(): Promise<void> {
  memoryStatus = null;
  await caches.default.delete(STATUS_CACHE_KEY);
}

export async function getCachedStatus(env: Env, ctx: ExecutionContext, fresh: boolean): Promise<Response> {
  // Admin actions bypass all colos' caches. Do not let an older in-flight cache rebuild replace this response.
  if (fresh) {
    return getStatus(env);
  }
  if (memoryStatus && memoryStatus.expiresAt > Date.now()) {
    return cachedResponse(memoryStatus.body);
  }
  const cached = await caches.default.match(STATUS_CACHE_KEY);
  if (cached) {
    return cached;
  }
  const response = await getStatus(env);
  const body = await response.text();
  memoryStatus = { body, expiresAt: Date.now() + STATUS_TTL_SECONDS * 1000 };
  ctx.waitUntil(caches.default.put(STATUS_CACHE_KEY, cachedResponse(body)));
  return cachedResponse(body);
}

function record(body: unknown): body is Record<string, unknown> {
  return typeof body === "object" && body !== null && !Array.isArray(body);
}

function questId(value: unknown): value is string {
  return typeof value === "string" && QUEST.test(value);
}

function eventId(value: unknown, allowZero = false): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= (allowZero ? 0 : 1);
}

export async function getStatus(env: Env, now?: number): Promise<Response> {
  // A slow older query must not look newer than a fresh response after an admin action.
  const at = now ?? Date.now();
  const cutoff = at - WINDOW_MS;
  const { results } = await env.DB.prepare(`
    WITH latest AS (
      SELECT e.* FROM reports r JOIN report_events e ON e.id = r.meaningful_id
    ), success AS (
      SELECT quest_id, path_hash, created_at,
        ROW_NUMBER() OVER (PARTITION BY quest_id, path_hash ORDER BY created_at DESC, id DESC) AS position
      FROM latest WHERE worked = 1
    ), problem AS (
      SELECT quest_id, path_hash,
        CASE WHEN json_valid(versions) THEN json_extract(versions, '$.Questionable') END AS version,
        ROW_NUMBER() OVER (PARTITION BY quest_id, path_hash ORDER BY created_at DESC, id DESC) AS position
      FROM latest WHERE worked = 0 AND resolved_at IS NULL
    ), counts AS (
      SELECT quest_id, path_hash, SUM(worked) AS worked, COUNT(*) AS total,
        SUM(CASE WHEN worked = 1 AND created_at >= ?1 THEN 1 ELSE 0 END) AS recentWorked,
        SUM(CASE WHEN worked = 0 AND resolved_at IS NULL AND created_at >= ?1 THEN 1 ELSE 0 END) AS recentProblems,
        MAX(CASE WHEN worked = 1 THEN created_at END) AS lastWorkedAt,
        MAX(CASE WHEN worked = 0 AND resolved_at IS NULL THEN created_at END) AS lastProblemAt
      FROM latest GROUP BY quest_id, path_hash
    ), history AS (
      SELECT quest_id, path_hash, MAX(created_at) AS lastReportAt
      FROM latest GROUP BY quest_id, path_hash
    ), keys AS (
      SELECT quest_id, path_hash FROM history UNION SELECT quest_id, path_hash FROM verifications
    )
    SELECT k.quest_id, k.path_hash, COALESCE(c.worked, 0) AS worked, COALESCE(c.total, 0) AS total,
      COALESCE(c.recentWorked, 0) AS recentWorked, COALESCE(c.recentProblems, 0) AS recentProblems,
      h.lastReportAt, c.lastWorkedAt, s.created_at AS fifthWorkedAt, c.lastProblemAt,
      p.version AS lastProblemVersion, v.verified_at AS verifiedAt
    FROM keys k
    LEFT JOIN counts c USING (quest_id, path_hash)
    LEFT JOIN history h USING (quest_id, path_hash)
    LEFT JOIN success s ON s.quest_id = k.quest_id AND s.path_hash = k.path_hash AND s.position = 5
    LEFT JOIN problem p ON p.quest_id = k.quest_id AND p.path_hash = k.path_hash AND p.position = 1
    LEFT JOIN verifications v USING (quest_id, path_hash)`)
    .bind(cutoff).all<StatusRow>();

  const quests: Record<string, Record<string, PathStatus>> = {};
  for (const { quest_id, path_hash, ...status } of results) {
    (quests[quest_id] ??= {})[path_hash] = status;
  }
  return reply({ at, quests });
}

export async function getReportPage(env: Env, quest: unknown): Promise<Response> {
  if (!questId(quest)) {
    return reply({ error: "quest" }, 400);
  }

  // Read the watermark and rows together. Verification never resolves reports newer than this view.
  const [snapshot, rows] = await env.DB.batch([
    env.DB.prepare("SELECT COALESCE(MAX(id), 0) AS snapshot FROM report_events"),
    env.DB.prepare(`
      SELECT e.*, CASE WHEN EXISTS (
        SELECT 1 FROM reports r WHERE r.meaningful_id = e.id
      ) THEN 1 ELSE 0 END AS current
      FROM report_events e WHERE e.quest_id = ? AND e.worked = 0
      ORDER BY e.id DESC LIMIT ?`).bind(quest, ADMIN_REPORT_LIMIT),
  ]);
  const reports = (rows.results as EventRow[]).map(row => ({
    id: row.id,
    path: row.path_hash,
    kind: row.kind,
    sequence: row.sequence,
    step: row.step,
    versions: row.versions ? JSON.parse(row.versions) : null,
    actions: row.actions ? JSON.parse(row.actions) : null,
    at: row.created_at,
    resolvedAt: row.resolved_at,
    resolvedBy: row.resolved_by,
    current: row.current === 1,
  }));
  return reply({ snapshot: (snapshot.results[0] as { snapshot: number }).snapshot, reports });
}

export async function resolveReport(env: Env, admin: string, body: unknown): Promise<Response> {
  if (!record(body) || !questId(body.quest) || !eventId(body.id)) {
    return reply({ error: "quest/id" }, 400);
  }
  const result = await env.DB.prepare(`
    UPDATE report_events SET resolved_at = COALESCE(resolved_at, ?), resolved_by = COALESCE(resolved_by, ?)
    WHERE quest_id = ? AND id = ? AND worked = 0`)
    .bind(Date.now(), admin, body.quest, body.id).run();
  if (result.meta.changes > 0) {
    await resetStatusCache();
  }
  return result.meta.changes > 0 ? reply({ ok: true }) : reply({ error: "report not found" }, 404);
}

export async function verifyPath(env: Env, admin: string, body: unknown): Promise<Response> {
  if (!record(body) || !questId(body.quest) || typeof body.path !== "string" || !PATH.test(body.path) ||
      !eventId(body.snapshot, true)) {
    return reply({ error: "quest/path/snapshot" }, 400);
  }
  const last = await env.DB.prepare("SELECT COALESCE(MAX(id), 0) AS id FROM report_events").first<{ id: number }>();
  if (body.snapshot > (last?.id ?? 0)) {
    return reply({ error: "snapshot is newer than reports" }, 409);
  }

  const now = Date.now();
  await env.DB.batch([
    env.DB.prepare("INSERT INTO paths (quest_id, path_hash, first_seen) VALUES (?, ?, ?) ON CONFLICT DO NOTHING")
      .bind(body.quest, body.path, now),
    env.DB.prepare(`
      UPDATE report_events SET resolved_at = ?, resolved_by = ?
      WHERE quest_id = ? AND path_hash = ? AND id <= ? AND worked = 0 AND resolved_at IS NULL`)
      .bind(now, admin, body.quest, body.path, body.snapshot),
    env.DB.prepare(`
      INSERT INTO verifications (quest_id, path_hash, verified_at, verified_by, through_id) VALUES (?, ?, ?, ?, ?)
      ON CONFLICT (quest_id, path_hash) DO UPDATE SET
        verified_at = excluded.verified_at, verified_by = excluded.verified_by, through_id = excluded.through_id`)
      .bind(body.quest, body.path, now, admin, body.snapshot),
  ]);
  await resetStatusCache();
  return reply({ ok: true });
}
