import { CHARACTER, QUEST } from "./validate";

export const MAX_ZIP_BYTES = 10 * 1024 * 1024;
export const ZIP_RETENTION_DAYS = 30;
const DAY_MS = 24 * 60 * 60 * 1000;
const LIST_LIMIT = 100;
// No 0/O, 1/I/L: players read the code off the chat and type it into Discord.
const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
const VERSION = /^\d{1,5}(\.\d{1,5}){1,3}$/;
export const ZIP_KINDS = ["path", "mtr", "death", "step", "other"] as const;

export interface ZipRow {
  code: string;
  quest_id: string | null;
  kind: string | null;
  version: string | null;
  size: number;
  created_at: number;
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

export function newCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return [...bytes].map(b => CODE_ALPHABET[b % CODE_ALPHABET.length]).join("");
}

// Reads the zip's central directory; only Questionable's own zips have a report.txt at the top.
export function hasReportTxt(bytes: Uint8Array): boolean {
  if (bytes.length < 22 || bytes[0] !== 0x50 || bytes[1] !== 0x4b || bytes[2] !== 0x03 || bytes[3] !== 0x04) {
    return false;
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let end = -1;
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 22 - 0xffff); i--) {
    if (view.getUint32(i, true) === 0x06054b50) {
      end = i;
      break;
    }
  }
  if (end < 0) {
    return false;
  }

  const count = view.getUint16(end + 10, true);
  let at = view.getUint32(end + 16, true);
  for (let i = 0; i < count; i++) {
    if (at + 46 > bytes.length || view.getUint32(at, true) !== 0x02014b50) {
      return false;
    }
    const nameLength = view.getUint16(at + 28, true);
    const extraLength = view.getUint16(at + 30, true);
    const commentLength = view.getUint16(at + 32, true);
    if (at + 46 + nameLength > bytes.length) {
      return false;
    }
    if (new TextDecoder().decode(bytes.subarray(at + 46, at + 46 + nameLength)) === "report.txt") {
      return true;
    }
    at += 46 + nameLength + extraLength + commentLength;
  }
  return false;
}

export async function postZip(request: Request, env: Env, url: URL,
  hashCharacter: (character: string) => Promise<string>): Promise<Response> {
  // Errors between quests have no quest.
  const quest = url.searchParams.get("quest");
  const character = url.searchParams.get("character") ?? "";
  const kind = url.searchParams.get("kind");
  const version = url.searchParams.get("version");
  if ((quest !== null && !QUEST.test(quest)) || !CHARACTER.test(character) ||
    (kind !== null && !(ZIP_KINDS as readonly string[]).includes(kind)) ||
    (version !== null && !VERSION.test(version))) {
    return json({ error: "invalid parameters" }, 400);
  }

  const declared = request.headers.get("Content-Length");
  if (declared !== null && Number(declared) > MAX_ZIP_BYTES) {
    return json({ error: "too large" }, 413);
  }
  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.byteLength > MAX_ZIP_BYTES) {
    return json({ error: "too large" }, 413);
  }
  if (!hasReportTxt(bytes)) {
    return json({ error: "not a Questionable zip" }, 400);
  }

  const hashed = await hashCharacter(character);
  const now = Date.now();
  for (let attempt = 0; attempt < 3; attempt++) {
    const code = newCode();
    const inserted = await env.DB.prepare(`
      INSERT INTO zips (code, character, quest_id, kind, version, size, created_at)
      VALUES (?, ?, ?, ?, ?, ?, ?) ON CONFLICT (code) DO NOTHING`)
      .bind(code, hashed, quest, kind, version, bytes.byteLength, now)
      .run();
    if (inserted.meta.changes === 1) {
      try {
        await env.ZIPS.put(code, bytes, { httpMetadata: { contentType: "application/zip" } });
      } catch (error) {
        await env.DB.prepare("DELETE FROM zips WHERE code = ?").bind(code).run();
        throw error;
      }
      return json({ code });
    }
  }
  return json({ error: "internal error" }, 500);
}

export async function listZips(env: Env, params: URLSearchParams): Promise<Response> {
  const columns = "code, quest_id, kind, version, size, created_at";
  const code = params.get("code");
  if (code !== null) {
    const { results } = await env.DB.prepare(`SELECT ${columns} FROM zips WHERE code = ?`).bind(code.toUpperCase()).all<ZipRow>();
    return json({ zips: results });
  }

  const where: string[] = [];
  const binds: (string | number)[] = [];
  const quest = params.get("quest");
  if (quest !== null) {
    where.push("quest_id = ?");
    binds.push(quest);
  }
  // "none" lists zips from clients that didn't send a problem yet.
  const kind = params.get("kind");
  if (kind === "none") {
    where.push("kind IS NULL");
  } else if (kind !== null) {
    if (!(ZIP_KINDS as readonly string[]).includes(kind)) {
      return json({ error: "invalid parameters" }, 400);
    }
    where.push("kind = ?");
    binds.push(kind);
  }
  // created_at:code of the last zip on the previous page; the code keeps zips from the same millisecond apart.
  const before = params.get("before");
  if (before !== null) {
    const match = /^(\d{1,15}):([A-Z0-9]{6})$/.exec(before);
    if (!match) {
      return json({ error: "invalid parameters" }, 400);
    }
    const at = Number(match[1]);
    where.push("(created_at < ? OR (created_at = ? AND code < ?))");
    binds.push(at, at, match[2]);
  }

  const filter = where.length > 0 ? `WHERE ${where.join(" AND ")}` : "";
  const { results } = await env.DB.prepare(
    `SELECT ${columns} FROM zips ${filter} ORDER BY created_at DESC, code DESC LIMIT ?`).bind(...binds, LIST_LIMIT).all<ZipRow>();
  const last = results.at(-1);
  return json({ zips: results, next: results.length === LIST_LIMIT && last ? `${last.created_at}:${last.code}` : null });
}

export async function downloadZip(env: Env, code: string): Promise<Response> {
  const object = /^[A-Z0-9]{6}$/.test(code) ? await env.ZIPS.get(code) : null;
  if (!object) {
    return json({ error: "not found" }, 404);
  }
  return new Response(object.body, {
    headers: {
      "Content-Type": "application/zip",
      "Content-Disposition": `attachment; filename="QST-${code}.zip"`,
    },
  });
}

export async function deleteZip(env: Env, code: string): Promise<Response> {
  if (!/^[A-Z0-9]{6}$/.test(code)) {
    return json({ error: "not found" }, 404);
  }
  // Object first: a row left behind is still pruned after 30 days, an orphaned object never is.
  await env.ZIPS.delete(code);
  const { meta } = await env.DB.prepare("DELETE FROM zips WHERE code = ?").bind(code).run();
  return meta.changes > 0 ? json({ deleted: code }) : json({ error: "not found" }, 404);
}

export async function pruneZips(env: Env, now: number): Promise<void> {
  const before = now - ZIP_RETENTION_DAYS * DAY_MS;
  const { results } = await env.DB.prepare("SELECT code FROM zips WHERE created_at < ?").bind(before).all<{ code: string }>();
  // R2 deletes at most 1000 keys per call.
  for (let i = 0; i < results.length; i += 1000) {
    await env.ZIPS.delete(results.slice(i, i + 1000).map(x => x.code));
  }
  await env.DB.prepare("DELETE FROM zips WHERE created_at < ?").bind(before).run();
}
