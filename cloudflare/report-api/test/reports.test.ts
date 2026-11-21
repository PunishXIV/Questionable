import { env, exports } from "cloudflare:workers";
import { createScheduledController } from "cloudflare:test";
import { beforeEach, describe, expect, it, vi } from "vitest";
import worker, { prune } from "../src/index";
import { getStatus, resetStatusCache, type PathStatus } from "../src/status";

const CHARACTER_A = "a".repeat(64);
const CHARACTER_B = "b".repeat(64);
const PATH_1 = "1".repeat(16);
const PATH_2 = "2".repeat(16);
const DAY = 24 * 60 * 60 * 1000;

type Counts = Record<string, Record<string, [number, number]>>;
let requestNumber = 0;

function post(body: unknown): Promise<Response> {
  return exports.default.fetch("https://reports.test/v1/reports", {
    method: "POST",
    headers: { "Content-Type": "application/json", "CF-Connecting-IP": `test-${++requestNumber}` },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

function report(character: string, path: string, outcome: "worked" | "problem", extra: object = {}) {
  return { character, quest: "1190", path, outcome, ...extra };
}

async function counts(): Promise<Counts> {
  return Object.fromEntries(Object.entries((await status()).quests).map(([quest, paths]) =>
    [quest, Object.fromEntries(Object.entries(paths).map(([path, s]) => [path, [s.worked, s.total]]))]));
}

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

beforeEach(async () => {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM reports"),
    env.DB.prepare("DELETE FROM report_events"),
    env.DB.prepare("DELETE FROM verifications"),
    env.DB.prepare("DELETE FROM paths"),
    env.DB.prepare("DELETE FROM admins"),
  ]);
  await resetStatusCache();
});

describe("reports", () => {
  it("counts worked and problem reports per path", async () => {
    expect((await post(report(CHARACTER_A, PATH_1, "worked"))).status).toBe(200);
    expect((await post(report(CHARACTER_B, PATH_1, "problem", { kind: "stuck", sequence: 1, step: 3 }))).status).toBe(200);

    expect(await counts()).toEqual({ "1190": { [PATH_1]: [1, 2] } });
  });

  it("replaces a character's earlier report on the same path", async () => {
    await post(report(CHARACTER_A, PATH_1, "worked"));
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "deaths" }));

    expect(await counts()).toEqual({ "1190": { [PATH_1]: [0, 1] } });
  });

  it("keeps paths apart", async () => {
    await post(report(CHARACTER_A, PATH_1, "worked"));
    await post(report(CHARACTER_A, PATH_2, "worked"));

    expect(await counts()).toEqual({ "1190": { [PATH_1]: [1, 1], [PATH_2]: [1, 1] } });
  });

  it("stores only a salted character hash", async () => {
    await post(report(CHARACTER_A, PATH_1, "worked"));

    const row = await env.DB.prepare("SELECT character FROM reports").first<{ character: string }>();
    expect(row?.character).toBe(await sha256Hex(`test-salt:${CHARACTER_A}`));
  });

  it.each([
    ["bad character", report("nope", PATH_1, "worked")],
    ["bad quest", { ...report(CHARACTER_A, PATH_1, "worked"), quest: "1; DROP TABLE reports" }],
    ["problem without kind", report(CHARACTER_A, PATH_1, "problem")],
    ["worked with kind", report(CHARACTER_A, PATH_1, "worked", { kind: "stuck" })],
    ["unknown kind", report(CHARACTER_A, PATH_1, "problem", { kind: "boring" })],
    ["step out of range", report(CHARACTER_A, PATH_1, "problem", { kind: "stuck", step: 999 })],
    ["too many actions", report(CHARACTER_A, PATH_1, "problem", { kind: "stuck", actions: Array(11).fill("x") })],
    ["not json", "{"],
  ])("rejects %s", async (_, body) => {
    expect((await post(body)).status).toBe(400);
  });

  it("rejects bodies over 8 KB", async () => {
    const body = report(CHARACTER_A, PATH_1, "problem", { kind: "other", actions: ["x".repeat(9000)] });
    expect((await post(body)).status).toBe(413);
  });

  it("accepts the other quest id forms", async () => {
    for (const quest of ["S12", "U3", "A5x3"]) {
      expect((await post({ ...report(CHARACTER_A, PATH_1, "worked"), quest })).status).toBe(200);
    }
  });
});

describe("admin reports", () => {
  async function addAdmin(name: string, token: string) {
    await env.DB.prepare("INSERT INTO admins (name, token_hash) VALUES (?, ?)").bind(name, await sha256Hex(token)).run();
  }

  function read(token?: string) {
    return exports.default.fetch("https://reports.test/v2/admin/reports?quest=1190", {
      headers: { "CF-Connecting-IP": `admin-${++requestNumber}`, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    });
  }

  it("needs a known token", async () => {
    await addAdmin("kage", "right-token");

    expect((await read()).status).toBe(401);
    expect((await read("wrong-token")).status).toBe(401);
  });

  it("lists problem reports without the character", async () => {
    await addAdmin("aly", "aly-token");
    await post(report(CHARACTER_A, PATH_1, "worked"));
    await post(report(CHARACTER_B, PATH_1, "problem", {
      kind: "questBattle", sequence: 2, step: 0, versions: { Questionable: "15.756.4.0", "Boss Mod": "7.5.8.2" },
      actions: ["MoveTo(1, 2, 3)", "QuestBattle(1045)"],
    }));

    const response = await read("aly-token");
    expect(response.status).toBe(200);
    const { reports } = (await response.json()) as { reports: Record<string, unknown>[] };
    expect(reports).toHaveLength(1);
    expect(reports[0]).toMatchObject({
      path: PATH_1, kind: "questBattle", sequence: 2, step: 0,
      versions: { Questionable: "15.756.4.0", "Boss Mod": "7.5.8.2" },
      actions: ["MoveTo(1, 2, 3)", "QuestBattle(1045)"],
    });
    expect(reports[0]).not.toHaveProperty("character");
  });
});

describe("pruning", () => {
  async function insert(path: string, createdAt: number, firstSeen: number) {
    await env.DB.batch([
      env.DB.prepare("INSERT OR IGNORE INTO paths (quest_id, path_hash, first_seen) VALUES ('1190', ?, ?)").bind(path, firstSeen),
      env.DB.prepare("INSERT INTO reports (character, quest_id, path_hash, worked, created_at) VALUES (?, '1190', ?, 1, ?)")
        .bind(crypto.randomUUID(), path, createdAt),
    ]);
  }

  async function remaining(): Promise<string[]> {
    const { results } = await env.DB.prepare("SELECT path_hash FROM reports ORDER BY path_hash").all<{ path_hash: string }>();
    return results.map(x => x.path_hash);
  }

  it("drops reports older than 180 days", async () => {
    const now = 1_000 * DAY;
    await insert(PATH_1, now - 181 * DAY, now - 181 * DAY);
    await insert(PATH_1, now - 10 * DAY, now - 181 * DAY);

    await prune(env, now);

    expect(await remaining()).toEqual([PATH_1]);
  });

  it("drops a replaced path 30 days after the new one appeared", async () => {
    const start = 1_000 * DAY;
    await insert(PATH_1, start, start);
    await insert(PATH_2, start + DAY, start + DAY);

    await prune(env, start + 20 * DAY);
    expect(await remaining()).toEqual([PATH_1, PATH_2]);

    await prune(env, start + 40 * DAY);
    expect(await remaining()).toEqual([PATH_2]);
  });

  it("keeps an older path that still gets reports", async () => {
    const start = 1_000 * DAY;
    await insert(PATH_1, start + 39 * DAY, start);
    await insert(PATH_2, start + DAY, start + DAY);

    await prune(env, start + 40 * DAY);

    expect(await remaining()).toEqual([PATH_1, PATH_2]);
  });

  it("runs from the cron trigger", async () => {
    await insert(PATH_1, 0, 0);

    await worker.scheduled(createScheduledController({ scheduledTime: 400 * DAY, cron: "17 3 * * *" }), env);

    expect(await remaining()).toEqual([]);
  });
});

type StatusReply = { at: number; quests: Record<string, Record<string, PathStatus>> };
type AdminPage = {
  snapshot: number;
  reports: { id: number; path: string; at: number; resolvedAt: number | null; resolvedBy: string | null; current: boolean }[];
};

async function status(now?: number): Promise<StatusReply> {
  const response = now === undefined
    ? await exports.default.fetch("https://reports.test/v2/status?fresh=1", {
      headers: { "CF-Connecting-IP": `status-${++requestNumber}` },
    })
    : await getStatus(env, now);
  expect(response.status).toBe(200);
  expect(response.headers.get("Cache-Control")).toBe("no-store");
  return await response.json() as StatusReply;
}

async function addStatusAdmin(): Promise<void> {
  await env.DB.prepare("INSERT INTO admins (name, token_hash) VALUES (?, ?)")
    .bind("kage", await sha256Hex("status-token")).run();
}

function adminRequest(route: string, body?: unknown, token = "status-token"): Promise<Response> {
  return exports.default.fetch(`https://reports.test/v2/admin/${route}`, {
    method: body === undefined ? "GET" : "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", "CF-Connecting-IP": `admin-${++requestNumber}` },
    body: body === undefined ? undefined : typeof body === "string" ? body : JSON.stringify(body),
  });
}

async function page(): Promise<AdminPage> {
  const response = await adminRequest("reports?quest=1190");
  expect(response.status).toBe(200);
  return await response.json() as AdminPage;
}

describe("report status", () => {
  it("serves the status page", async () => {
    const response = await exports.default.fetch("https://reports.test/");
    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toContain("text/html");
    expect(await response.text()).toContain("fetch(\"/v2/status\")");
  });

  it("rate-limits fresh reads", async () => {
    let last = 0;
    for (let i = 0; i < 31; i++) {
      last = (await exports.default.fetch("https://reports.test/v2/status?fresh=1", {
        headers: { "CF-Connecting-IP": "flood" },
      })).status;
    }
    expect(last).toBe(429);
  });

  it("keeps the query-start watermark when an older read finishes late", async () => {
    const started = 1_000 * DAY;
    const clock = vi.spyOn(Date, "now").mockReturnValue(started);
    const delayed = {
      DB: {
        prepare: () => ({
          bind: (cutoff: number) => {
            expect(cutoff).toBe(started - 30 * DAY);
            return {
              all: async () => {
                await Promise.resolve();
                clock.mockReturnValue(started + 5_000);
                return { results: [] };
              },
            };
          },
        }),
      },
    } as unknown as Env;
    try {
      const response = await getStatus(delayed);
      expect((await response.json() as StatusReply).at).toBe(started);
    } finally {
      clock.mockRestore();
    }
  });

  it("caches ordinary reads while fresh reads bypass stale shared snapshots", async () => {
    const cached = await exports.default.fetch("https://reports.test/v2/status");
    expect(cached.headers.get("Cache-Control")).toBe("public, max-age=600");
    expect((await cached.json() as StatusReply).quests).toEqual({});
    await post(report(CHARACTER_A, PATH_1, "worked"));
    const stillCached = await exports.default.fetch("https://reports.test/v2/status");
    expect((await stillCached.json() as StatusReply).quests).toEqual({});
    expect((await status()).quests["1190"][PATH_1].worked).toBe(1);
    const cachedAgain = await exports.default.fetch("https://reports.test/v2/status");
    expect((await cachedAgain.json() as StatusReply).quests).toEqual({});
  });

  it("returns an empty successful snapshot rather than inventing health evidence", async () => {
    expect((await status()).quests).toEqual({});
  });

  it("ignores manual stops without erasing the earlier meaningful report", async () => {
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "playerStopped" }));
    await post(report(CHARACTER_B, PATH_2, "problem", { kind: "playerStopped" }));

    const health = (await status()).quests["1190"];
    expect(health[PATH_1]).toMatchObject({ worked: 0, total: 1, recentWorked: 0, recentProblems: 1 });
    expect(health[PATH_1].lastProblemAt).not.toBeNull();
    expect(health[PATH_2]).toBeUndefined();
  });

  it("keeps concurrent submissions and latest pointers consistent", async () => {
    await Promise.all([
      post(report(CHARACTER_A, PATH_1, "worked")),
      post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" })),
    ]);
    const latest = await env.DB.prepare("SELECT id, worked FROM report_events ORDER BY id DESC LIMIT 1")
      .first<{ id: number; worked: number }>();
    const pointers = await env.DB.prepare("SELECT event_id, meaningful_id, worked FROM reports")
      .first<{ event_id: number; meaningful_id: number; worked: number }>();
    expect(pointers).toEqual({ event_id: latest!.id, meaningful_id: latest!.id, worked: latest!.worked });
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ total: 1, worked: latest!.worked });
    expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM report_events").first<number>("count")).toBe(2);
  });

  it("counts five characters rather than five runs and exposes exact expiry timestamps", async () => {
    const now = 1_000 * DAY;
    const cutoff = now - 30 * DAY;
    for (let i = 1; i <= 5; i++) {
      const character = i.toString().repeat(64);
      await post(report(character, PATH_1, "worked"));
      await env.DB.prepare("UPDATE report_events SET created_at = ? WHERE id = (SELECT MAX(id) FROM report_events)")
        .bind(cutoff + i - 1).run();
    }
    const initial = (await status(now)).quests["1190"][PATH_1];
    expect(initial).toMatchObject({ worked: 5, total: 5, recentWorked: 5, recentProblems: 0,
      lastWorkedAt: cutoff + 4, fifthWorkedAt: cutoff });
    expect((await status(now + 1)).quests["1190"][PATH_1].recentWorked).toBe(4);

    await post(report("1".repeat(64), PATH_1, "worked"));
    const repeated = (await status()).quests["1190"][PATH_1];
    expect(repeated.total).toBe(5);
    expect(repeated.worked).toBe(5);
  });

  it("keeps older evidence but counts recent problems only inside the thirty-day boundary", async () => {
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const now = 1_000 * DAY;
    const cutoff = now - 30 * DAY;
    await env.DB.prepare("UPDATE report_events SET created_at = ?").bind(cutoff).run();
    expect((await status(now)).quests["1190"][PATH_1].recentProblems).toBe(1);
    const old = (await status(now + 1)).quests["1190"][PATH_1];
    expect(old.recentProblems).toBe(0);
    expect(old.lastProblemAt).toBe(cutoff);
    expect(old.lastReportAt).toBe(cutoff);
  });

  it("keeps each path and its verification separate", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "worked"));
    await post(report(CHARACTER_A, PATH_2, "problem", { kind: "stuck" }));
    const snapshot = (await page()).snapshot;
    expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot })).status).toBe(200);
    const health = (await status()).quests["1190"];
    expect(health[PATH_1].verifiedAt).not.toBeNull();
    expect(health[PATH_1].recentWorked).toBe(1);
    expect(health[PATH_2].verifiedAt).toBeNull();
    expect(health[PATH_2].recentProblems).toBe(1);
  });
});

describe("admin status actions", () => {
  it("requires a known token for reads and both mutations", async () => {
    await addStatusAdmin();
    for (const token of ["", "wrong-token"]) {
      expect((await adminRequest("reports?quest=1190", undefined, token)).status).toBe(401);
      expect((await adminRequest("resolve", { quest: "1190", id: 1 }, token)).status).toBe(401);
      expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: 0 }, token)).status).toBe(401);
    }
  });

  it("resolves one report without turning it into a success or changing legacy counts", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const first = (await page()).reports[0];
    expect((await adminRequest("resolve", { quest: "1190", id: first.id })).status).toBe(200);
    const health = (await status()).quests["1190"][PATH_1];
    expect(health).toMatchObject({ worked: 0, total: 1, recentWorked: 0, recentProblems: 0, lastProblemAt: null, verifiedAt: null });
    const resolved = (await page()).reports[0];
    expect(resolved.resolvedAt).not.toBeNull();
    expect(resolved.resolvedBy).toBe("kage");
    expect(resolved.current).toBe(true);
    expect(await counts()).toEqual({ "1190": { [PATH_1]: [0, 1] } });
  });

  it("preserves resolved history when the same character reports a new problem", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const original = (await page()).reports[0];
    await adminRequest("resolve", { quest: "1190", id: original.id });
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "deaths" }));
    const rows = (await page()).reports;
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ current: true, resolvedAt: null });
    expect(rows[1]).toMatchObject({ id: original.id, current: false, resolvedBy: "kage" });
    expect((await status()).quests["1190"][PATH_1].recentProblems).toBe(1);
    expect(JSON.stringify(rows)).not.toContain(CHARACTER_A);
    expect(rows[0]).not.toHaveProperty("character");
  });

  it("an old resolve button never resolves the same character's replacement report", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const old = (await page()).reports[0];
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "deaths" }));
    await adminRequest("resolve", { quest: "1190", id: old.id });
    expect((await status()).quests["1190"][PATH_1].recentProblems).toBe(1);
    expect((await page()).reports[0].resolvedAt).toBeNull();
  });

  it("verifies the viewed snapshot atomically and leaves intervening reports open", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const viewed = await page();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "deaths" }));
    await post(report(CHARACTER_B, PATH_1, "problem", { kind: "wrongTarget" }));
    expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: viewed.snapshot })).status).toBe(200);
    const health = (await status()).quests["1190"][PATH_1];
    expect(health.verifiedAt).not.toBeNull();
    expect(health.recentProblems).toBe(2);
    const rows = (await page()).reports;
    expect(rows.filter(x => x.resolvedAt !== null).map(x => x.id)).toEqual([viewed.reports[0].id]);
  });

  it("verifies all viewed problems and a later report reopens the path", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    await post(report(CHARACTER_B, PATH_1, "problem", { kind: "deaths" }));
    await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: (await page()).snapshot });
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ recentProblems: 0, worked: 0, lastProblemAt: null });
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    expect((await status()).quests["1190"][PATH_1].recentProblems).toBe(1);
  });

  it("allows verification before any reports without inventing successful runs", async () => {
    await addStatusAdmin();
    expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: 0 })).status).toBe(200);
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ worked: 0, total: 0, recentWorked: 0, recentProblems: 0, lastReportAt: null });
    expect((await status()).quests["1190"][PATH_1].verifiedAt).not.toBeNull();
  });

  it("rolls resolution back if saving the verification fails", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const viewed = await page();
    await env.DB.prepare("CREATE TRIGGER fail_verification BEFORE INSERT ON verifications BEGIN SELECT RAISE(ABORT, 'test rollback'); END").run();
    try {
      expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: viewed.snapshot })).status).toBe(500);
    } finally {
      await env.DB.prepare("DROP TRIGGER fail_verification").run();
    }
    expect((await page()).reports[0].resolvedAt).toBeNull();
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ recentProblems: 1, verifiedAt: null });
  });

  it.each([
    ["resolve", { quest: "1190", id: -1 }],
    ["resolve", { quest: "1190", id: 1.5 }],
    ["resolve", { quest: "1190", id: Number.MAX_SAFE_INTEGER + 1 }],
    ["verify", { quest: "1190", path: "wrong", snapshot: 0 }],
    ["verify", { quest: "1190", path: PATH_1, snapshot: -1 }],
    ["verify", { quest: "1190' OR 1=1", path: PATH_1, snapshot: 0 }],
    ["verify", "{"],
  ])("validates %s mutation inputs", async (route, body) => {
    await addStatusAdmin();
    expect((await adminRequest(route, body)).status).toBe(400);
  });

  it("rejects future snapshots, oversized bodies and mismatched quests", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    const viewed = await page();
    expect((await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: viewed.snapshot + 1 })).status).toBe(409);
    expect((await adminRequest("resolve", { quest: "999", id: viewed.reports[0].id })).status).toBe(404);
    expect((await adminRequest("verify", "x".repeat(8193))).status).toBe(413);
  });

  it("prunes event history and verification under existing retention rules", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: (await page()).snapshot });
    const now = Date.now();
    await env.DB.batch([
      env.DB.prepare("UPDATE reports SET created_at = ?").bind(now - 181 * DAY),
      env.DB.prepare("UPDATE report_events SET created_at = ?").bind(now - 181 * DAY),
      env.DB.prepare("UPDATE verifications SET verified_at = ?").bind(now - 181 * DAY),
      env.DB.prepare("UPDATE paths SET first_seen = ?").bind(now - 181 * DAY),
    ]);
    await prune(env, now);
    expect((await status()).quests).toEqual({});
    expect((await page()).reports).toEqual([]);
  });

  it("prunes a replaced path's history and verification but keeps the new path", async () => {
    await addStatusAdmin();
    await post(report(CHARACTER_A, PATH_1, "problem", { kind: "stuck" }));
    await adminRequest("verify", { quest: "1190", path: PATH_1, snapshot: (await page()).snapshot });
    await post(report(CHARACTER_A, PATH_2, "worked"));
    const now = Date.now();
    await env.DB.prepare("UPDATE paths SET first_seen = ? WHERE path_hash = ?").bind(now - 40 * DAY, PATH_1).run();
    await env.DB.prepare("UPDATE paths SET first_seen = ? WHERE path_hash = ?").bind(now - 31 * DAY, PATH_2).run();
    await env.DB.prepare("UPDATE reports SET created_at = ? WHERE path_hash = ?").bind(now - 31 * DAY, PATH_1).run();
    await prune(env, now);
    const health = (await status()).quests["1190"];
    expect(health[PATH_1]).toBeUndefined();
    expect(health[PATH_2]).toBeDefined();
    expect((await page()).reports).toEqual([]);
  });
});

describe("legacy database upgrade", () => {
  it("backfills existing reports and captures writes from the old Worker after migration", async () => {
    // Rebuild only the local test database from the real migration statements.
    await env.DB.batch(["reports", "report_events", "verifications", "paths", "admins"]
      .map(table => env.DB.prepare(`DROP TABLE ${table}`)));
    await env.DB.batch(env.TEST_MIGRATIONS[0].queries.map((query: string) => env.DB.prepare(query)));
    await env.DB.prepare("INSERT INTO paths (quest_id, path_hash, first_seen) VALUES ('1190', ?, ?)").bind(PATH_1, Date.now()).run();
    await env.DB.prepare("INSERT INTO reports (character, quest_id, path_hash, worked, kind, created_at) VALUES (?, '1190', ?, 0, 'stuck', ?)")
      .bind(CHARACTER_A, PATH_1, Date.now()).run();
    await env.DB.batch(env.TEST_MIGRATIONS[1].queries.map((query: string) => env.DB.prepare(query)));

    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ total: 1, recentProblems: 1 });
    const linked = await env.DB.prepare("SELECT event_id FROM reports").first<{ event_id: number }>();
    expect(linked?.event_id).toBeGreaterThan(0);
    // Legacy code knows nothing about event_id or report_events, and still records the replacement.
    await env.DB.prepare("UPDATE reports SET worked = 1, kind = NULL, created_at = ? WHERE quest_id = '1190'").bind(Date.now()).run();
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ total: 1, worked: 1, recentProblems: 0 });
    expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM report_events").first<number>("count")).toBe(2);
    expect(await counts()).toEqual({ "1190": { [PATH_1]: [1, 1] } });
    await env.DB.prepare("UPDATE reports SET worked = 0, kind = 'playerStopped', created_at = ? WHERE quest_id = '1190'").bind(Date.now()).run();
    expect((await status()).quests["1190"][PATH_1]).toMatchObject({ total: 1, worked: 1, recentProblems: 0 });
  });
});
