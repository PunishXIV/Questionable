import { DASHBOARD } from "./dashboard";
import { downloadZip, listZips, postZip, pruneZips } from "./logs";
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

// One IPv6 customer usually owns a whole /64, so the limit counts those addresses together.
export function rateKey(ip: string | null): string {
  if (!ip) {
    return "local";
  }
  if (!ip.includes(":")) {
    return ip;
  }

  const [head, tail] = ip.split("::");
  const first = head ? head.split(":") : [];
  const last = tail ? tail.split(":") : [];
  const groups = tail === undefined ? first : [...first, ...Array(8 - first.length - last.length).fill("0"), ...last];
  return groups.slice(0, 4).map(group => group.replace(/^0+(?=.)/, "")).join(":");
}

async function rateLimited(limiter: RateLimit | undefined, request: Request): Promise<Response | null> {
  if (!limiter) {
    return null;
  }

  const key = rateKey(request.headers.get("CF-Connecting-IP"));
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

interface Admin {
  name: string;
  role: string;
}

async function findAdmin(request: Request, env: Env): Promise<Admin | null> {
  const header = request.headers.get("Authorization");
  if (!header?.startsWith("Bearer ")) {
    return null;
  }

  const tokenHash = await sha256Hex(header.slice("Bearer ".length).trim());
  return await env.DB.prepare("SELECT name, role FROM admins WHERE token_hash = ?")
    .bind(tokenHash)
    .first<Admin>();
}

export async function prune(env: Env, now: number): Promise<void> {
  const replacedBefore = now - REPLACED_PATH_DAYS * DAY_MS;
  await env.DB.batch([
    env.DB.prepare("DELETE FROM reports WHERE created_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    env.DB.prepare("DELETE FROM report_events WHERE created_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    env.DB.prepare("DELETE FROM verifications WHERE verified_at < ?").bind(now - RETENTION_DAYS * DAY_MS),
    // A path counts as replaced once a newer path has been around long enough and it gets no reports itself any more.
    // reports goes last: the other two read it to see whether the path is still in use.
    ...["report_events", "verifications", "reports"].map(table => env.DB.prepare(`
      DELETE FROM ${table} WHERE EXISTS (
        SELECT 1 FROM paths old JOIN paths newer
          ON newer.quest_id = old.quest_id AND newer.first_seen > old.first_seen
        WHERE old.quest_id = ${table}.quest_id AND old.path_hash = ${table}.path_hash
          AND newer.first_seen < ?1)
        AND NOT EXISTS (
          SELECT 1 FROM reports recent
          WHERE recent.quest_id = ${table}.quest_id AND recent.path_hash = ${table}.path_hash
            AND recent.created_at >= ?1)`)
      .bind(replacedBefore)),
    env.DB.prepare(`DELETE FROM paths
      WHERE NOT EXISTS (SELECT 1 FROM reports WHERE reports.quest_id = paths.quest_id AND reports.path_hash = paths.path_hash)
        AND NOT EXISTS (SELECT 1 FROM report_events WHERE report_events.quest_id = paths.quest_id AND report_events.path_hash = paths.path_hash)
        AND NOT EXISTS (SELECT 1 FROM verifications WHERE verifications.quest_id = paths.quest_id AND verifications.path_hash = paths.path_hash)
        AND first_seen < ?`)
      .bind(now - RETENTION_DAYS * DAY_MS),
  ]);
  await resetStatusCache();
  await pruneZips(env, now);
}

export default {
  async fetch(request, env, ctx): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/v1/reports" && request.method === "POST") {
        return await postReport(request, env);
      }

      if (url.pathname === "/v2/logs" && request.method === "POST") {
        if (!env.CHARACTER_SALT) {
          return json({ error: "not configured" }, 503);
        }
        const limited = await rateLimited(env.ZIP_IP_LIMITER, request);
        return limited ?? await postZip(request, env, url, character => sha256Hex(`${env.CHARACTER_SALT}:${character}`));
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
        // Uncached reads run the full aggregate, so only admins get them; everyone else gets the cache.
        const wantsFresh = url.searchParams.get("fresh") === "1";
        const limited = wantsFresh ? await rateLimited(env.ADMIN_IP_LIMITER, request) : null;
        return limited ?? await getCachedStatus(env, ctx, wantsFresh && await findAdmin(request, env) !== null);
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
        if (url.pathname === "/v2/admin/logs" && request.method === "GET") {
          return await listZips(env, url.searchParams.get("quest"), url.searchParams.get("code"));
        }
        if (url.pathname.startsWith("/v2/admin/logs/") && request.method === "GET") {
          return await downloadZip(env, url.pathname.slice("/v2/admin/logs/".length).toUpperCase());
        }
        if (request.method === "POST" && (url.pathname === "/v2/admin/resolve" || url.pathname === "/v2/admin/verify")) {
          if (admin.role !== "admin") {
            return json({ error: "read-only token" }, 403);
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
          return url.pathname.endsWith("/resolve")
            ? await resolveReport(env, admin.name, body)
            : await verifyPath(env, admin.name, body);
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
