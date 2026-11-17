-- Authored with LLM assistance, changes must be reviewed and owned by a human.
-- Initial version reviewed and owned by @Kagekazu
CREATE TABLE report_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
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
    resolved_at INTEGER,
    resolved_by TEXT
);

INSERT INTO report_events (character, quest_id, path_hash, worked, kind, sequence, step, versions, actions, created_at)
SELECT character, quest_id, path_hash, worked, kind, sequence, step, versions, actions, created_at
FROM reports ORDER BY created_at, quest_id, path_hash, character;

ALTER TABLE reports ADD COLUMN event_id INTEGER;
ALTER TABLE reports ADD COLUMN meaningful_id INTEGER;
UPDATE reports SET event_id = (
    SELECT id FROM report_events e
    WHERE e.character = reports.character AND e.quest_id = reports.quest_id AND e.path_hash = reports.path_hash
);
UPDATE reports SET meaningful_id = event_id WHERE worked = 1 OR kind != 'playerStopped';
CREATE INDEX reports_by_meaningful ON reports (meaningful_id);

CREATE INDEX events_by_path ON report_events (quest_id, path_hash, id);
CREATE INDEX events_by_quest ON report_events (quest_id, id);
CREATE INDEX events_by_character ON report_events (character, quest_id, path_hash, id);
CREATE INDEX events_by_age ON report_events (created_at);

-- Capture writes from both the old Worker and the new one during a rolling deployment.
CREATE TRIGGER reports_insert_event AFTER INSERT ON reports
BEGIN
    INSERT INTO report_events (character, quest_id, path_hash, worked, kind, sequence, step, versions, actions, created_at)
    VALUES (NEW.character, NEW.quest_id, NEW.path_hash, NEW.worked, NEW.kind, NEW.sequence, NEW.step, NEW.versions, NEW.actions, NEW.created_at);
    UPDATE reports SET event_id = (
        SELECT MAX(id) FROM report_events
        WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash
    ), meaningful_id = CASE WHEN NEW.worked = 1 OR NEW.kind != 'playerStopped' THEN (
        SELECT MAX(id) FROM report_events
        WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash
    ) ELSE NULL END
    WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash;
END;

CREATE TRIGGER reports_update_event AFTER UPDATE OF worked, kind, sequence, step, versions, actions, created_at ON reports
BEGIN
    INSERT INTO report_events (character, quest_id, path_hash, worked, kind, sequence, step, versions, actions, created_at)
    VALUES (NEW.character, NEW.quest_id, NEW.path_hash, NEW.worked, NEW.kind, NEW.sequence, NEW.step, NEW.versions, NEW.actions, NEW.created_at);
    UPDATE reports SET event_id = (
        SELECT MAX(id) FROM report_events
        WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash
    ), meaningful_id = CASE WHEN NEW.worked = 1 OR NEW.kind != 'playerStopped' THEN (
        SELECT MAX(id) FROM report_events
        WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash
    ) ELSE OLD.meaningful_id END
    WHERE character = NEW.character AND quest_id = NEW.quest_id AND path_hash = NEW.path_hash;
END;

CREATE TABLE verifications (
    quest_id TEXT NOT NULL,
    path_hash TEXT NOT NULL,
    verified_at INTEGER NOT NULL,
    verified_by TEXT NOT NULL,
    through_id INTEGER NOT NULL,
    PRIMARY KEY (quest_id, path_hash)
);
