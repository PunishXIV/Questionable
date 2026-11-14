# qst-reports

Cloudflare Worker behind Questionable's quest reports: players (opt-in) report whether a quest path worked, and Questionable shows how many runs of each path worked.

## What is stored

One row per character per quest path:

- a salted hash of a hash of the character id (no names, no ids, no IP addresses),
- the quest id and a 16-character fingerprint of its path,
- worked or problem; for a problem: the kind, sequence and step, plugin and path data versions, and Questionable's last few actions.

Reports are deleted after 180 days, and 30 days after a newer path for the same quest first shows up. The client's IP address is only used by the rate limiter.

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
| `GET /v1/counts` | anyone | `{ quests: { "<quest>": { "<path>": [worked, total] } } }`, cached for 10 minutes. |
| `GET /v1/admin/reports?quest=<quest>` | admins | Problem reports for a quest, newest first. `Authorization: Bearer <token>`. |

Problem kinds: `stuck`, `deaths`, `questBattle`, `missingPath`, `playerStopped`, `wrongTarget`, `other`.

## Develop

```sh
npm test           # vitest in the Workers runtime, local D1
npm run check      # regenerate worker-configuration.d.ts and type-check
```
