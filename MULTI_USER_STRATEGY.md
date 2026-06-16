# Multi-User Database Access Strategy

## Problem
Multiple users will access the same SQLite database file via a shared file system. We need a mechanism to notify users when someone else has the database open, and optionally lock them out.

## Chosen Approach: Sidecar Lock File with Heartbeat

Place a `<dbname>.lock` file next to the database containing JSON:

```json
{
  "user": "jsmith",
  "machine": "OFFICE-PC",
  "pid": 12345,
  "heartbeat": "2026-03-03T14:22:00Z"
}
```

### Behavior

- **On open**: Check for the lock file. If it exists and the heartbeat is fresh (< 60s old), warn: *"Database appears to be in use by jsmith on OFFICE-PC."* Offer a "force open" button if the user knows the other session is stale.
- **Background timer** (~every 30s): Update the heartbeat timestamp continuously while the app is open, regardless of whether the user is actively accessing the DB. This is about **presence**, not activity.
- **On close**: Delete the lock file.
- **Stale lock recovery**: If the heartbeat is older than 60s, assume the previous user crashed and take ownership.

### Why This Approach

- Zero DB schema changes
- Trivial to implement
- No SQLite contention
- Adequate for a small (2-3) user count

## Alternative Considered: Lock Table in the DB

A `_sessions` table with `user`, `machine`, `heartbeat` columns. Each instance inserts/updates a row on a timer and queries for other active sessions.

- **Pro**: Self-contained, supports showing all connected users.
- **Con**: Adds write contention to the DB; SQLite over network shares can be flaky with concurrent writers.

Rejected in favor of the simpler sidecar approach.

## WAL Mode

SQLite's default journaling locks the entire database file — readers block writers and vice versa. WAL (Write-Ahead Logging) writes to a separate `-wal` file first, then checkpoints back to the main DB. This allows concurrent readers and one writer without blocking each other.

Enabled with:
```sql
PRAGMA journal_mode=WAL;
```

WAL creates two sidecar files (`<db>-wal` and `<db>-shm`) that rely on shared-memory primitives which **don't work reliably over network shares** (SMB/NFS). File locks can be delayed, lost, or not properly exclusive when relayed over a network file sharing protocol.

## Network Share Caveat

A "network share" is when a folder on one computer's disk is made accessible to other computers over the network (e.g., `\\SERVER\schedules\` or a mapped drive like `S:\`). SQLite's locking relies on OS-level file locks, and SMB doesn't always relay those faithfully.

If multi-user access over a network share proves unreliable, the robust next step is a small server process that sits on the machine where the database lives and handles all SQLite access locally, with clients communicating over the network rather than touching the file directly.
