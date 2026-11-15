"""Field-test helper: plays a colleague who saves a change to the shared database D.

Normally run for you by Edit-TestDatabaseAsColleague in the FieldTest module, which hands it a LOCAL COPY of D and
then renames the edited copy over D in one step. This script never touches a network share: it refuses any path
that starts with two slashes or backslashes.

Usage:
    python colleague.py colleague "<path of a LOCAL copy of the database>"

What it does: appends " (colleague)" to the last name of the alphabetically first instructor, in both the readable
last_name column and the JSON data column, so the change is easy to spot after TermPoint reopens the database.
Run it again to add another " (colleague)".
"""

import os
import sqlite3
import sys


def colleague(db_path: str) -> None:
    """Changes one instructor's last name, the way a colleague's save would change D.

    Args:
        db_path: Full path of the LOCAL database copy to edit.

    Raises:
        SystemExit: when the path is on a network share, the file does not exist, or it has no instructors.
    """
    if db_path.startswith("\\\\") or db_path.startswith("//"):
        sys.exit(f"Refusing a network path (work on a local copy): {db_path}")
    if not os.path.isfile(db_path):
        sys.exit(f"Not found: {db_path}")

    # Rollback-journal mode (Python's default): leaves no -wal/-shm beside the copy afterwards.
    con = sqlite3.connect(db_path)
    try:
        row = con.execute(
            "SELECT id, last_name FROM Instructors ORDER BY last_name, first_name LIMIT 1"
        ).fetchone()
        if row is None:
            sys.exit("The database has no instructors to change.")

        instructor_id, last_name = row
        new_last = f"{last_name} (colleague)"
        con.execute(
            "UPDATE Instructors SET last_name = ?, data = json_set(data, '$.lastName', ?) WHERE id = ?",
            (new_last, new_last, instructor_id),
        )
        con.commit()
        print(f'Colleague changed instructor "{last_name}" to "{new_last}".')
    finally:
        con.close()


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "colleague":
        sys.exit(__doc__)
    colleague(sys.argv[2])
