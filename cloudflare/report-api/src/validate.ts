export const PROBLEM_KINDS = [
  "stuck",
  "deaths",
  "questBattle",
  "missingPath",
  "playerStopped",
  "wrongTarget",
  "other",
] as const;

export type ProblemKind = (typeof PROBLEM_KINDS)[number];

export interface Report {
  character: string;
  quest: string;
  path: string;
  worked: boolean;
  kind: ProblemKind | null;
  sequence: number | null;
  step: number | null;
  versions: Record<string, string> | null;
  actions: string[] | null;
}

export type Validation = { ok: true; report: Report } | { ok: false; error: string };

const CHARACTER = /^[0-9a-f]{64}$/;
// QST element ids (ElementId.ToString): "1190", "S12", "U3", "N4", "C5", "A5x3".
// Quest numbers fit in 16 bits; longer ones would only pad the status list.
export const QUEST = /^[A-Z]?\d{1,5}(x\d{1,3})?$/;
export const PATH = /^[0-9a-f]{16}$/;
const VERSION_KEY = /^[A-Za-z0-9 ._&'()-]{1,40}$/;
const PRINTABLE = /^[\x20-\x7E]*$/;

const MAX_VERSIONS = 16;
const MAX_VERSION_LENGTH = 32;
const MAX_ACTIONS = 10;
const MAX_ACTION_LENGTH = 160;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function optionalByte(value: unknown): number | null | undefined {
  if (value === undefined || value === null) {
    return null;
  }

  return Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 255 ? (value as number) : undefined;
}

export function validateReport(body: unknown): Validation {
  if (!isRecord(body)) {
    return { ok: false, error: "body must be an object" };
  }

  const { character, quest, path, outcome, kind } = body;
  if (typeof character !== "string" || !CHARACTER.test(character)) {
    return { ok: false, error: "character" };
  }

  if (typeof quest !== "string" || !QUEST.test(quest)) {
    return { ok: false, error: "quest" };
  }

  if (typeof path !== "string" || !PATH.test(path)) {
    return { ok: false, error: "path" };
  }

  if (outcome !== "worked" && outcome !== "problem") {
    return { ok: false, error: "outcome" };
  }

  const worked = outcome === "worked";
  if (worked ? kind !== undefined && kind !== null : !PROBLEM_KINDS.includes(kind as ProblemKind)) {
    return { ok: false, error: "kind" };
  }

  const sequence = optionalByte(body.sequence);
  const step = optionalByte(body.step);
  if (sequence === undefined || step === undefined) {
    return { ok: false, error: "sequence/step" };
  }

  let versions: Record<string, string> | null = null;
  if (body.versions !== undefined && body.versions !== null) {
    if (!isRecord(body.versions)) {
      return { ok: false, error: "versions" };
    }

    const entries = Object.entries(body.versions);
    if (entries.length > MAX_VERSIONS ||
        entries.some(([key, value]) => !VERSION_KEY.test(key) || typeof value !== "string" ||
            value.length > MAX_VERSION_LENGTH || !PRINTABLE.test(value))) {
      return { ok: false, error: "versions" };
    }

    versions = Object.fromEntries(entries) as Record<string, string>;
  }

  let actions: string[] | null = null;
  if (body.actions !== undefined && body.actions !== null) {
    if (!Array.isArray(body.actions) || body.actions.length > MAX_ACTIONS ||
        body.actions.some(x => typeof x !== "string" || x.length > MAX_ACTION_LENGTH || !PRINTABLE.test(x))) {
      return { ok: false, error: "actions" };
    }

    actions = body.actions as string[];
  }

  return {
    ok: true,
    report: {
      character,
      quest,
      path,
      worked,
      kind: worked ? null : (kind as ProblemKind),
      sequence,
      step,
      versions,
      actions,
    },
  };
}
