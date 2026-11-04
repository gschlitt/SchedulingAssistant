"""Field-test helper: plays "Alice on another computer" by writing (or deleting) a database's lock file.

Normally run for you by Set-TestLock in the FieldTest module (which supplies the shared test database's path from
the config file). It only writes or deletes the lock file beside the database; it never opens the database itself,
so it is safe to point at a database on a network share.

Usage:
    python fake_lock.py takeover "<path of the .db>"
        Alice takes the lock now (fresh heartbeat). Run it WHILE TermPoint has the database open for editing:
        the writer's next save finds Alice's lock and it is moved to read-only.
    python fake_lock.py stale "<path of the .db>"
        Alice's lock, last heard from 10 minutes ago. Run it while TermPoint is CLOSED, then open the database:
        TermPoint offers to take over the inactive lock.
    python fake_lock.py clear "<path of the .db>"
        Deletes the lock file (only do this while TermPoint is closed everywhere).

ONLY EVER RUN THIS ON THE TEST DATABASE.
"""

import json
import os
import sys
import uuid
from datetime import datetime, timedelta, timezone


def lock_path(db_path: str) -> str:
    """Returns the lock file TermPoint uses for a database: same folder and name, extension .lock.

    Args:
        db_path: Full path of the database.

    Returns:
        The full path of its lock file.
    """
    return os.path.splitext(db_path)[0] + ".lock"


def write_alice_lock(db_path: str, minutes_ago: int) -> None:
    """Writes a lock file held by Alice on another computer.

    Args:
        db_path: Full path of the database.
        minutes_ago: How long ago Alice's heartbeat was last renewed (0 = right now).

    Raises:
        OSError: when the lock file cannot be written.
    """
    beat = datetime.now(timezone.utc) - timedelta(minutes=minutes_ago)
    stamp = beat.strftime("%Y-%m-%dT%H:%M:%S.%f0Z")
    data = {
        "Username": "Alice",
        "Machine": "ALICE-PC",
        "SessionGuid": str(uuid.uuid4()),
        "Acquired": stamp,
        "Heartbeat": stamp,
        "ProcessId": 4242,
    }
    with open(lock_path(db_path), "w", encoding="utf-8") as f:
        json.dump(data, f)
    print(f"Wrote Alice's lock (heartbeat {minutes_ago} min ago): {lock_path(db_path)}")


def main() -> None:
    """Parses the command line and runs the chosen action.

    Raises:
        SystemExit: on a bad command line or when the database does not exist.
    """
    if len(sys.argv) != 3 or sys.argv[1] not in ("takeover", "stale", "clear"):
        sys.exit(__doc__)
    action, db = sys.argv[1], sys.argv[2]
    if not os.path.isfile(db):
        sys.exit(f"Not found: {db}")
    if action == "takeover":
        write_alice_lock(db, minutes_ago=0)
    elif action == "stale":
        write_alice_lock(db, minutes_ago=10)
    else:
        path = lock_path(db)
        if os.path.exists(path):
            os.remove(path)
            print(f"Deleted {path}")
        else:
            print("No lock file.")


if __name__ == "__main__":
    main()
