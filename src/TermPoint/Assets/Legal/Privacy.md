# TermPoint — Privacy Statement

*Last updated: June 2026*

TermPoint is a course-scheduling visualization tool for university administrators. It is designed to keep all data local and minimize external communication. This statement explains what data TermPoint stores, what it sends over the network, and what it does not do.

## Data Storage

All scheduling data (sections, instructors, rooms, semesters, and related entities) is stored in a local SQLite database file on the user's computer or network drive. TermPoint does not upload scheduling data to any cloud service.

Application preferences (window settings, recent file paths, display options) are stored in a small JSON file under the application's AppData directory. In MSIX (Store) installations, Windows virtualizes this path into the package's own container. This file never leaves the machine.

## Crash & Error Reporting (BugSnag)

TermPoint uses BugSnag to collect crash and error reports so the developers can identify and fix problems. When an error occurs, the following information may be sent to BugSnag's servers:

- Stack trace — the sequence of function calls that led to the error
- App version and release stage (production or development)
- Operating system name and version
- Anonymous install identifier — a randomly generated ID stored locally, used to group errors from the same installation. This ID cannot be linked back to any user or institution.
- Breadcrumbs — a short log of recent application events leading up to the error (e.g. "opened database", "switched semester", "saved changes"). These contain operational context only, never scheduling data or personal information.
- Session data — whether the app session experienced a crash, used to calculate a stability score

**What is NOT sent to BugSnag:**

- User names, email addresses, or any other personal information
- Institution name or any institutional data
- Scheduling data (sections, instructors, rooms, courses, or student information)
- File paths to the database
- Any content entered into the application

## Update Checks (non-Store builds only)

If TermPoint is installed outside the Microsoft Store (e.g. from a direct download), the app periodically checks GitHub Releases for newer versions. This check sends a standard HTTPS request to GitHub's API; no personal or institutional data is included.

If TermPoint is installed from the Microsoft Store, the Store handles updates and TermPoint makes no update-check requests itself.

## Workload Mailer

The Workload Mailer feature composes emails using the system's default email client (via `mailto:` links). TermPoint does not send email directly — it opens a draft in the local mail application, and the user decides whether to send it. No email content passes through TermPoint's servers or any third-party service.

## What TermPoint Does NOT Do

- No account or login required — TermPoint has no user accounts and no authentication system.
- No cloud sync — all data stays on locally chosen drives.
- No analytics or telemetry beyond the crash reporting described above.
- No advertising or tracking.
- No access to contacts, calendar, camera, microphone, or location.

## Data Retention

BugSnag retains error data according to its plan terms. On the free tier, error data is retained for 7 days. TermPoint's local data (database and settings) remains on the machine until the user deletes it.
