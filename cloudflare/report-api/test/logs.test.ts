import { env, exports } from "cloudflare:workers";
import { beforeEach, describe, expect, it } from "vitest";
import { prune } from "../src/index";
import { hasReportTxt, MAX_ZIP_BYTES } from "../src/logs";

const CHARACTER = "c".repeat(64);
const DAY = 24 * 60 * 60 * 1000;
let requestNumber = 0;

// Stored (uncompressed) zip; the worker only reads the central directory.
function zip(files: Record<string, string>): Uint8Array {
  const encoder = new TextEncoder();
  const local: number[] = [];
  const central: number[] = [];
  const u16 = (out: number[], v: number) => out.push(v & 0xff, (v >> 8) & 0xff);
  const u32 = (out: number[], v: number) => out.push(v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, (v >>> 24) & 0xff);
  for (const [name, text] of Object.entries(files)) {
    const nameBytes = encoder.encode(name);
    const data = encoder.encode(text);
    const offset = local.length;
    u32(local, 0x04034b50); u16(local, 20); u16(local, 0); u16(local, 0); u32(local, 0); u32(local, 0);
    u32(local, data.length); u32(local, data.length); u16(local, nameBytes.length); u16(local, 0);
    local.push(...nameBytes, ...data);
    u32(central, 0x02014b50); u16(central, 20); u16(central, 20); u16(central, 0); u16(central, 0); u32(central, 0);
    u32(central, 0); u32(central, data.length); u32(central, data.length); u16(central, nameBytes.length);
    u16(central, 0); u16(central, 0); u16(central, 0); u16(central, 0); u32(central, 0); u32(central, offset);
    central.push(...nameBytes);
  }
  const end: number[] = [];
  const count = Object.keys(files).length;
  u32(end, 0x06054b50); u16(end, 0); u16(end, 0); u16(end, count); u16(end, count);
  u32(end, central.length); u32(end, local.length); u16(end, 0);
  return new Uint8Array([...local, ...central, ...end]);
}

const QST_ZIP = zip({ "report.txt": "QST v16.0.0.3", "questionable-recent.log": "line" });
// Written by .NET's ZipArchive (deflated, like the plugin's), not by the helper above.
const DOTNET_ZIP = Uint8Array.from(atob(
  "UEsDBBQAAAAIAEi3e11050NrJgAAACYAAAAKAAAAcmVwb3J0LnR4dAsMDlEoMzTTMwBCY4WS1OIShZzMvFSFtPwihaLUgvyiEr2SihIAUEsDBBQAAAAIAEi3e132sR+QMwAAADQAAAAYAAAAZGFsYW11ZC1xdWVzdGlvbmFibGUubG9nCwwOUSgzNNMzAEJjhZLU4hKFnMy8VIW0/CKFlMScxNzSFN3CUqBwZn5eYlJOql5OfjoAUEsDBBQAAAAIAEi3e108qVElMgAAADMAAAAXAAAAcXVlc3Rpb25hYmxlLXJlY2VudC5sb2cLDA5RKDM00zMAQmOFktTiEoWczLxUhbT8IoXCUiA3Mz8vMSknVbcoNTk1r0QvJz8dAFBLAQIUABQAAAAIAEi3e11050NrJgAAACYAAAAKAAAAAAAAAAAAAAAAAAAAAAByZXBvcnQudHh0UEsBAhQAFAAAAAgASLd7XfaxH5AzAAAANAAAABgAAAAAAAAAAAAAAAAATgAAAGRhbGFtdWQtcXVlc3Rpb25hYmxlLmxvZ1BLAQIUABQAAAAIAEi3e108qVElMgAAADMAAAAXAAAAAAAAAAAAAAAAALcAAABxdWVzdGlvbmFibGUtcmVjZW50LmxvZ1BLBQYAAAAAAwADAMMAAAAeAQAAAAA="),
  c => c.charCodeAt(0));

function upload(body: BodyInit, query = `quest=1130&character=${CHARACTER}&kind=stuck&version=16.0.0.3`): Promise<Response> {
  return exports.default.fetch(`https://reports.test/v2/logs?${query}`, {
    method: "POST",
    headers: { "Content-Type": "application/zip", "CF-Connecting-IP": `zip-${++requestNumber}` },
    body,
  });
}

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

async function token(name: string, role: string): Promise<string> {
  const value = `${name}-token`;
  await env.DB.prepare("INSERT INTO admins (name, token_hash, role) VALUES (?, ?, ?)")
    .bind(name, await sha256Hex(value), role).run();
  return value;
}

function admin(path: string, bearer: string, init: RequestInit = {}): Promise<Response> {
  return exports.default.fetch(`https://reports.test${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${bearer}`, "CF-Connecting-IP": `admin-${++requestNumber}`, ...init.headers },
  });
}

beforeEach(async () => {
  const { results } = await env.DB.prepare("SELECT code FROM zips").all<{ code: string }>();
  if (results.length > 0) {
    await env.ZIPS.delete(results.map(x => x.code));
  }
  await env.DB.batch([env.DB.prepare("DELETE FROM zips"), env.DB.prepare("DELETE FROM admins")]);
});

describe("zip uploads", () => {
  it("stores a zip and gives back a code", async () => {
    const response = await upload(QST_ZIP);
    expect(response.status).toBe(200);
    const { code } = await response.json<{ code: string }>();
    expect(code).toMatch(/^[A-HJKMNP-Z2-9]{6}$/);

    const row = await env.DB.prepare("SELECT quest_id, kind, version, size, character FROM zips WHERE code = ?")
      .bind(code).first<{ quest_id: string; kind: string; version: string; size: number; character: string }>();
    expect(row).toMatchObject({ quest_id: "1130", kind: "stuck", version: "16.0.0.3", size: QST_ZIP.byteLength });
    expect(row!.character).toBe(await sha256Hex(`test-salt:${CHARACTER}`));
    expect(new Uint8Array(await (await env.ZIPS.get(code))!.arrayBuffer())).toEqual(QST_ZIP);
  });

  it("rejects anything that isn't a Questionable zip", async () => {
    expect((await upload("just text")).status).toBe(400);
    expect((await upload(zip({ "other.txt": "x" }))).status).toBe(400);
    expect((await upload(QST_ZIP, `quest=nope&character=${CHARACTER}`)).status).toBe(400);
    expect((await upload(QST_ZIP, "quest=1130&character=short")).status).toBe(400);
    expect((await upload(QST_ZIP, `quest=1130&character=${CHARACTER}&kind=nonsense`)).status).toBe(400);
    expect((await env.DB.prepare("SELECT COUNT(*) AS n FROM zips").first<{ n: number }>())!.n).toBe(0);
  });

  it("rejects zips over the size limit", async () => {
    const big = new Uint8Array(MAX_ZIP_BYTES + 1);
    big.set(QST_ZIP);
    expect((await upload(big)).status).toBe(413);
  });

  it("accepts a zip without a quest", async () => {
    const response = await upload(DOTNET_ZIP, `character=${CHARACTER}&kind=stuck`);
    expect(response.status).toBe(200);
    const { code } = await response.json<{ code: string }>();
    expect(await env.DB.prepare("SELECT quest_id FROM zips WHERE code = ?").bind(code).first<string>("quest_id")).toBeNull();
  });

  it("finds report.txt only in a real central directory", () => {
    expect(hasReportTxt(QST_ZIP)).toBe(true);
    expect(hasReportTxt(DOTNET_ZIP)).toBe(true);
    expect(hasReportTxt(new TextEncoder().encode("PK\x03\x04 report.txt"))).toBe(false);
  });
});

describe("zip admin", () => {
  it("lists and downloads zips for admins and viewers", async () => {
    const { code } = await (await upload(QST_ZIP)).json<{ code: string }>();
    await upload(QST_ZIP, `quest=5215&character=${CHARACTER}`);
    const viewer = await token("knightmore", "viewer");

    const latest = await (await admin("/v2/admin/logs", viewer)).json<{ zips: { code: string }[] }>();
    expect(latest.zips).toHaveLength(2);
    const byQuest = await (await admin("/v2/admin/logs?quest=1130", viewer)).json<{ zips: { code: string }[] }>();
    expect(byQuest.zips.map(x => x.code)).toEqual([code]);
    const byCode = await (await admin(`/v2/admin/logs?code=${code.toLowerCase()}`, viewer)).json<{ zips: { code: string }[] }>();
    expect(byCode.zips.map(x => x.code)).toEqual([code]);

    const download = await admin(`/v2/admin/logs/${code}`, viewer);
    expect(download.status).toBe(200);
    expect(download.headers.get("Content-Disposition")).toContain(`QST-${code}.zip`);
    expect(new Uint8Array(await download.arrayBuffer())).toEqual(QST_ZIP);
    expect((await admin("/v2/admin/logs/ZZZZZZ", viewer)).status).toBe(404);
  });

  it("needs a token", async () => {
    expect((await admin("/v2/admin/logs", "wrong")).status).toBe(401);
  });

  it("keeps viewers from resolving or verifying", async () => {
    const viewer = await token("knightmore", "viewer");
    const response = await admin("/v2/admin/resolve", viewer, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ quest: "1130", id: 1 }),
    });
    expect(response.status).toBe(403);
  });
});

describe("zip cleanup", () => {
  it("deletes zips older than 30 days from both stores", async () => {
    const { code: old } = await (await upload(QST_ZIP)).json<{ code: string }>();
    const { code: fresh } = await (await upload(QST_ZIP)).json<{ code: string }>();
    await env.DB.prepare("UPDATE zips SET created_at = ? WHERE code = ?").bind(Date.now() - 31 * DAY, old).run();

    await prune(env, Date.now());

    const { results } = await env.DB.prepare("SELECT code FROM zips").all<{ code: string }>();
    expect(results.map(x => x.code)).toEqual([fresh]);
    expect(await env.ZIPS.get(old)).toBeNull();
    expect(await env.ZIPS.get(fresh)).not.toBeNull();
  });
});
