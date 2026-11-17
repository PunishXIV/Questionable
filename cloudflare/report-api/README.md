# qst-reports

Cloudflare Worker behind Questionable's quest reports: players (opt-in) report whether a quest path worked, and Questionable shows how many runs of each path worked.

## What is stored

The latest result per character per quest path, plus report history:

- a salted hash of a hash of the character id (no names, no ids, no IP addresses),
- the quest id and a 16-character fingerprint of its path,
- worked or problem; for a problem: the kind, sequence and step, plugin and path data versions, and Questionable's last few actions.

Reports are deleted after 180 days, and 30 days after a newer path for the same quest first shows up. The client's IP address is only used by the rate limiter.

History records keep their own IDs. Resolving an old report therefore cannot accidentally resolve a later report from the same character. Admin verification records the admin, time and viewed report cutoff for one exact path fingerprint; it does not add successful player reports.

## Set up

```sh
npm install
npx wrangler d1 create qst-reports           # copy the database_id into wrangler.jsonc
npm run db:migrate:remote
npx wrangler secret put CHARACTER_SALT       # any long random string; never change it
npm run deploy
```

## Admins

Each admin has their own token. Only its SHA-256 is stored.

Add an admin, or give one a new token (PowerShell; set `$name`, then send them the token from the clipboard):

```powershell
$name = 'aly'; $b = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $t = -join ($b | % { $_.ToString('x2') }); $h = -join ([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($t)) | % { $_.ToString('x2') }); npx wrangler d1 execute qst-reports --remote --command "INSERT INTO admins (name, token_hash) VALUES ('$name', '$h') ON CONFLICT(name) DO UPDATE SET token_hash = excluded.token_hash"; $t | Set-Clipboard; "Token for $name is on the clipboard"
```

Remove an admin:

```sh
npx wrangler d1 execute qst-reports --remote --command "DELETE FROM admins WHERE name = 'aly'"
```

PowerShell for a random salt: `$b = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | % { $_.ToString('x2') })`.

## API

| Route | Who | |
| --- | --- | --- |
| `POST /v1/reports` | players | `{ character, quest, path, outcome: "worked" \| "problem", kind?, sequence?, step?, versions?, actions? }`. 30 per minute per IP. |
| `GET /` | anyone | Status page: quests with issues, needing verification and verified, by the plugin's rules. Quest names come from XIVAPI in the viewer's browser. |
| `GET /v2/status` | anyone | `{ at, quests: { "<quest>": { "<path>": status } } }`; timestamps are Unix milliseconds. No character or admin identities. Shared cache for 10 minutes. `?fresh=1` bypasses it after an admin action or a newly reported problem; 30 per minute per IP. |
| `GET /v2/admin/reports?quest=<quest>` | admins | `{ snapshot, reports }`, including historical problem reports, report IDs, resolution time/admin and whether each is the current meaningful report for that character/path. |
| `POST /v2/admin/resolve` | admins | `{ quest, id }`; resolves that report ID only, preserving history. |
| `POST /v2/admin/verify` | admins | `{ quest, path, snapshot }`; records verification and atomically resolves that path's problems through the viewed snapshot. Newer reports stay open. |

Problem kinds: `stuck`, `deaths`, `questBattle`, `missingPath`, `playerStopped`, `wrongTarget`, `other`.

The v2 status has `worked`, `total`, `recentWorked`, `recentProblems`, `lastReportAt`, `lastWorkedAt`, `fifthWorkedAt`, `lastProblemAt` and `verifiedAt`. Missing timestamps are null. Recent counts use an inclusive 30-day window. A manual stop is ignored before choosing each character's latest meaningful report, so it cannot erase earlier evidence. Resolved problems remain in the totals/history but are not unresolved problems and never become successful runs.

The client gives recent unresolved problems priority over verification, then uses five recent successful character reports or a verification within 30 days for green. Older or insufficient reports are orange; no evidence is grey. `fifthWorkedAt` and `lastProblemAt` let the client age those decisions accurately between refreshes.

Status aggregation reads each character/path's latest meaningful pointer, not the full 180-day event history. Ordinary reads share the 10-minute cache; a new successful run does not force every client to rebuild it. Fresh reads are not cached and do not overwrite shared snapshots, so a delayed cache rebuild cannot replace the acting admin's response. Mutations also invalidate the current location's status cache; other locations age out normally.

## Upgrade an existing deployment

Apply migration `0002_report_status.sql` before deploying the updated Worker, then deploy the matching plugin client. The migration backfills existing reports and adds triggers that also capture writes from the old Worker during rollout. The new client must treat a missing v2 endpoint as unavailable data, not an empty report set.

Use `npm run db:migrate:local` and the tests below for local verification. Remote migration and deployment are separate maintainer actions; neither is part of mirror validation.

## Develop

```sh
npm test           # vitest in the Workers runtime, local D1
npm run check      # regenerate worker-configuration.d.ts and type-check
```
