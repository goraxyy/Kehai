-- Playtest v2 (C1.1): the rows beside the session zips in R2. Apply with
--   npx wrangler d1 migrations apply kehai-playtest --local     (this Mac, for wrangler dev)
--   npx wrangler d1 migrations apply kehai-playtest --remote    (the cloud)
-- Every time is an ISO 8601 UTC string. A session's id is its zip's path: <round>/<tester>/<launch>.

CREATE TABLE testers (
  id         TEXT PRIMARY KEY,          -- the game's automatic ID: a label, not a login
  first_seen TEXT NOT NULL,
  nickname   TEXT                       -- optional, typed at the end
);

CREATE TABLE sessions (
  id               TEXT PRIMARY KEY,
  tester           TEXT NOT NULL,
  round            TEXT NOT NULL,
  launch           TEXT NOT NULL,
  build            TEXT NOT NULL DEFAULT '',
  schema           INTEGER NOT NULL DEFAULT 0,
  consent_version  TEXT NOT NULL,       -- 'v1' when an older build sent no consent header
  consent_training INTEGER NOT NULL,    -- 1 only when the tester agreed to training Karen
  collected        TEXT NOT NULL,       -- first receipt: Q5's six months count from here
  expires          TEXT NOT NULL,
  zip_key          TEXT,
  zip_bytes        INTEGER,
  zip_sha256       TEXT,
  zip_received     TEXT,
  summary_received TEXT,
  summary_sha256   TEXT,
  computer         TEXT,
  fps              REAL,
  minutes          REAL,
  ended            TEXT,
  deleted          TEXT,                -- 'expired' or 'request': a tombstone, kept for the Windows copies
  deleted_at       TEXT
);
CREATE INDEX sessions_tester ON sessions (tester);
CREATE INDEX sessions_expires ON sessions (expires);

CREATE TABLE shifts (
  session       TEXT NOT NULL,
  n             INTEGER NOT NULL,
  seconds       REAL,
  clocked_out   INTEGER,
  caught        INTEGER,
  spotted       INTEGER,
  closest_m     REAL,
  trick_sounds  INTEGER,
  lowest_energy REAL,
  PRIMARY KEY (session, n)
);

CREATE TABLE answers (
  session  TEXT PRIMARY KEY,
  taps     TEXT,                        -- the 4 taps, as a JSON array
  a1       TEXT,
  a2       TEXT,
  a3       TEXT,
  nickname TEXT
);

CREATE TABLE moments (
  session  TEXT NOT NULL,
  n        INTEGER NOT NULL,
  shift    INTEGER,
  replay_s REAL,
  note     TEXT,
  PRIMARY KEY (session, n)
);

CREATE TABLE events (
  id       TEXT PRIMARY KEY,             -- made by the game, so a retried send counts once
  tester   TEXT NOT NULL,
  session  TEXT,
  round    TEXT NOT NULL,
  build    TEXT NOT NULL DEFAULT '',
  kind     TEXT NOT NULL,
  at       TEXT NOT NULL,
  received TEXT NOT NULL
);
CREATE INDEX events_tester ON events (tester);
CREATE INDEX events_received ON events (received);

CREATE TABLE summaries (
  session TEXT PRIMARY KEY,
  text    TEXT NOT NULL,
  written TEXT NOT NULL
);

CREATE TABLE signups (
  email        TEXT PRIMARY KEY,
  at           TEXT NOT NULL,
  unsubscribed TEXT
);

-- Downloads are counted per day and platform only: nothing about who downloaded.
CREATE TABLE downloads (
  day      TEXT NOT NULL,
  platform TEXT NOT NULL,
  count    INTEGER NOT NULL,
  PRIMARY KEY (day, platform)
);

-- A deletion request: this tester's later uploads are refused.
CREATE TABLE deletions (
  tester TEXT PRIMARY KEY,
  at     TEXT NOT NULL
);

-- The sign-up rate limit: a salted hash of the address, never the address itself.
CREATE TABLE rate (
  key    TEXT PRIMARY KEY,
  hour   TEXT NOT NULL,
  count  INTEGER NOT NULL
);
