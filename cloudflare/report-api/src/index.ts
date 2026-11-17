import { DASHBOARD } from "./dashboard";
import { validateReport } from "./validate";
import { getCachedStatus, getReportPage, resetStatusCache, resolveReport, verifyPath } from "./status";

const MAX_BODY_BYTES = 8 * 1024;
const DAY_MS = 24 * 60 * 60 * 1000;
const RETENTION_DAYS = 180;
// Reports for a quest path are dropped this long after a newer path for the same quest first appeared.
const REPLACED_PATH_DAYS = 30;

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

export async function prune(env: Env, now: number): Promise<void> {
  const replacedBefore = now - REPLACED_PATH_DAYS * DAY_MS;
  await env.DB.batch([
    env.DB.prepare("DELETE FROM reports WHERE created_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    env.DB.prepare("DELETE FROM report_events WHERE created_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    env.DB.prepare("DELETE FROM verifications WHERE verified_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    // A path counts as replaced once a newer path for the same quest has been around long enough.
    env.DB.prepare(`
      DELETE FROM reports WHERE EXISTS (
        SELECT 1 FROM paths old JOIN paths newer
          ON newer.quest_id = old.quest_id AND newer.first_seen > old.first_seen
        WHERE old.quest_id = reports.quest_id AND old.path_hash = reports.path_hash
          AND newer.first_seen < ?)`)
      .bind(replacedBefore),
    env.DB.prepare(`
      DELETE FROM report_events WHERE EXISTS (
        SELECT 1 FROM paths old JOIN paths newer
          ON newer.quest_id = old.quest_id AND newer.first_seen > old.first_seen
        WHERE old.quest_id = report_events.quest_id AND old.path_hash = report_events.path_hash
          AND newer.first_seen < ?)`)
      .bind(replacedBefore),
    env.DB.prepare(`
      DELETE FROM verifications WHERE EXISTS (
        SELECT 1 FROM paths old JOIN paths newer
          ON newer.quest_id = old.quest_id AND newer.first_seen > old.first_seen
        WHERE old.quest_id = verifications.quest_id AND old.path_hash = verifications.path_hash
          AND newer.first_seen < ?)`)
      .bind(replacedBefore),
    env.DB.prepare(`DELETE FROM paths
      WHERE NOT EXISTS (SELECT 1 FROM reports WHERE reports.quest_id = paths.quest_id AND reports.path_hash = paths.path_hash)
        AND NOT EXISTS (SELECT 1 FROM report_events WHERE report_events.quest_id = paths.quest_id AND report_events.path_hash = paths.path_hash)
        AND NOT EXISTS (SELECT 1 FROM verifications WHERE verifications.quest_id = paths.quest_id AND verifications.path_hash = paths.path_hash)
        AND first_seen < ?`)
      .bind(now - RETENTION_DAYS * DAY_MS),
  ]);
  await resetStatusCache();
}

export default {
  async fetch(request, env, ctx): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/v1/reports" && request.method === "POST") {
        return await postReport(request, env);
      }

      if (url.pathname === "/" && request.method === "GET") {
        return new Response(DASHBOARD, {
          headers: {
            "Content-Type": "text/html; charset=utf-8",
            "Cache-Control": "public, max-age=3600",
            "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
              "connect-src 'self' https://v2.xivapi.com",
          },
        });
      }

      if (url.pathname === "/v2/status" && request.method === "GET") {
        const fresh = url.searchParams.get("fresh") === "1";
        const limited = fresh ? await rateLimited(env.ADMIN_IP_LIMITER, request) : null;
        return limited ?? await getCachedStatus(env, ctx, fresh);
      }

      if (url.pathname.startsWith("/v2/admin/")) {
        const limited = await rateLimited(env.ADMIN_IP_LIMITER, request);
        if (limited) {
          return limited;
        }
        const admin = await findAdmin(request, env);
        if (!admin) {
          return json({ error: "unauthorized" }, 401);
        }
        if (url.pathname === "/v2/admin/reports" && request.method === "GET") {
          return await getReportPage(env, url.searchParams.get("quest"));
        }
        if (request.method === "POST" && (url.pathname === "/v2/admin/resolve" || url.pathname === "/v2/admin/verify")) {
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
          return url.pathname.endsWith("/resolve")
            ? await resolveReport(env, admin, body)
            : await verifyPath(env, admin, body);
        }
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
