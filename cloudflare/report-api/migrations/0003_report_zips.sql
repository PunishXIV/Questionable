-- Troubleshooting zips players opted in to upload; the file itself is in R2 under its code.
CREATE TABLE zips (
    code TEXT PRIMARY KEY,
    character TEXT NOT NULL,
    quest_id TEXT,
    kind TEXT,
    version TEXT,
    size INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE INDEX zips_by_quest ON zips (quest_id, created_at);
CREATE INDEX zips_by_age ON zips (created_at);

-- 'viewer' tokens can read reports and zips but not resolve or verify.
ALTER TABLE admins ADD COLUMN role TEXT NOT NULL DEFAULT 'admin';
