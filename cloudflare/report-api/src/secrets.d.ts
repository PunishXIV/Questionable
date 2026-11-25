// Secrets aren't in wrangler.jsonc, so `wrangler types` doesn't know them.
interface Env {
  CHARACTER_SALT: string;
}

declare namespace Cloudflare {
  interface Env {
    CHARACTER_SALT: string;
  }
}
