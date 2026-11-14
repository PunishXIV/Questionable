import { env, exports } from "cloudflare:workers";
import { createScheduledController } from "cloudflare:test";
import { beforeEach, describe, expect, it } from "vitest";
import worker, { prune, resetCountsCache } from "../src/index";

const CHARACTER_A = "a".repeat(64);
const CHARACTER_B = "b".repeat(64);
const PATH_1 = "1".repeat(16);
const PATH_2 = "2".repeat(16);
const DAY = 24 * 60 * 60 * 1000;

type Counts = Record<string, Record<string, [number, number]>>;

function post(body: unknown): Promise<Response> {
  return exports.default.fetch("https://reports.test/v1/reports", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

function report(character: string, path: string, outcome: "worked" | "problem", extra: object = {}) {
  return { character, quest: "1190", path, outcome, ...extra };
}

async function counts(): Promise<Counts> {
  const response = await exports.default.fetch("https://reports.test/v1/counts");
  expect(response.status).toBe(200);
  return ((await response.json()) as { quests: Counts }).quests;
}

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

beforeEach(async () => {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM reports"),
    env.DB.prepare("DELETE FROM paths"),
    env.DB.prepare("DELETE FROM admins"),
  ]);
  await resetCountsCache();
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
    return exports.default.fetch("https://reports.test/v1/admin/reports?quest=1190", {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
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

  it("runs from the cron trigger", async () => {
    await insert(PATH_1, 0, 0);

    await worker.scheduled(createScheduledController({ scheduledTime: 400 * DAY, cron: "17 3 * * *" }), env);

    expect(await remaining()).toEqual([]);
  });
});
