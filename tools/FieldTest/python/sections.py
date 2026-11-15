"""Lists the field-test sections (section codes containing a pattern, default ZZZT) in each database COPY given,
plus each copy's integrity check.

Usage: py -3.11 -I sections.py [--pattern ZZZT] <copy.db> [<copy.db> ...]

The copies are opened READ-WRITE with plain sqlite3.connect so that a -wal file beside a copy is applied (a copy of
a working database is meaningless without its -wal). That is only safe on copies, so any path starting with two
slashes or backslashes (a network share) is refused. Imports nothing from its own folder, so it works under -I.
"""

import argparse
import os
import sqlite3
import sys


def describe(path: str, pattern: str) -> bool:
    """Prints the integrity check, the section count and every matching section of one copy.

    Args:
        path: Full path of a LOCAL database copy.
        pattern: Text to look for inside section codes (case-insensitive, plain text, no wildcards).

    Returns:
        True when the copy was read; False when it was refused, missing or unreadable (a message was printed).
    """
    if path.startswith("\\\\") or path.startswith("//"):
        print(f"{path}\n  REFUSED: network path (work on a copy)")
        return False
    if not os.path.isfile(path):
        print(f"{path}\n  NOT FOUND")
        return False
    try:
        con = sqlite3.connect(path)
        try:
            integrity = con.execute("PRAGMA integrity_check").fetchone()[0]
            total = con.execute("SELECT COUNT(*) FROM Sections").fetchone()[0]
            found = [f"{c} {s}" for c, s in con.execute(
                "SELECT course_code, section_code FROM Sections "
                "WHERE instr(lower(section_code), lower(?)) > 0 ORDER BY section_code", (pattern,))]
        finally:
            con.close()
    except sqlite3.Error as ex:
        print(f"{path}\n  ERROR: {ex}")
        return False
    print(f"{path}\n  integrity={integrity}  sections={total}  {pattern}: {found or 'none'}")
    return True


def main() -> int:
    """Parses the command line and describes every copy.

    Returns:
        0 when every copy was read, 1 otherwise.
    """
    parser = argparse.ArgumentParser(description="List field-test sections in database copies.")
    parser.add_argument("--pattern", default="ZZZT", help="text to look for in section codes (default ZZZT)")
    parser.add_argument("paths", nargs="+", help="local database copies")
    args = parser.parse_args()
    results = [describe(p, args.pattern) for p in args.paths]
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
