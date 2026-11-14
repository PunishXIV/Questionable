-- One report per character per quest path; a newer report from the same character replaces it.
CREATE TABLE reports (
    character TEXT NOT NULL,
    quest_id TEXT NOT NULL,
    path_hash TEXT NOT NULL,
    worked INTEGER NOT NULL,
    kind TEXT,
    sequence INTEGER,
    step INTEGER,
    versions TEXT,
    actions TEXT,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (character, quest_id, path_hash)
);

CREATE INDEX reports_by_path ON reports (quest_id, path_hash);
CREATE INDEX reports_by_age ON reports (created_at);

-- When each quest path was first reported, so reports for replaced paths can be pruned.
CREATE TABLE paths (
    quest_id TEXT NOT NULL,
    path_hash TEXT NOT NULL,
    first_seen INTEGER NOT NULL,
    PRIMARY KEY (quest_id, path_hash)
);

-- Admins who may read problem details; only a SHA-256 of each token is stored.
CREATE TABLE admins (
    name TEXT PRIMARY KEY,
    token_hash TEXT NOT NULL UNIQUE
);
