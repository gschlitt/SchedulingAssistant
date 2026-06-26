# TermPoint — Privacy Statement

*Last updated: June 2026*

## Overview

TermPoint is a course-scheduling visualization tool for university administrators. It is designed to keep your data local and minimize external communication. This statement explains what data TermPoint stores, what it sends over the network, and what it does not do.

## Data Storage

All scheduling data (sections, instructors, rooms, semesters, and related entities) is stored in a **local SQLite database file** on your computer or network drive. TermPoint does not upload your scheduling data to any cloud service.

Application preferences (window settings, recent file paths, display options) are stored in a small JSON file in your local AppData folder (`%APPDATA%\TermPoint\settings.json`). This file never leaves your machine.

## Crash & Error Reporting (BugSnag)

TermPoint uses **BugSnag** (https://www.bugsnag.com) to collect crash and error reports so we can identify and fix problems. When an error occurs, the following information may be sent to BugSnag's servers:

- **Stack trace** — the sequence of function calls that led to the error
- **App version** and **release stage** (production or development)
- **Operating system** name and version
- **Anonymous install identifier** — a randomly generated ID stored locally, used to group errors from the same installation. This ID cannot be linked back to you or your institution.
- **Breadcrumbs** — a short log of recent application events leading up to the error (e.g. "opened database", "switched semester", "saved changes"). These contain operational context only, never scheduling data or personal information.
- **Session data** — whether the app session experienced a crash, used to calculate a stability score

**What is NOT sent to BugSnag:**
- Your name, email address, or any other personal information
- Institution name or any institutional data
- Scheduling data (sections, instructors, rooms, courses, or student information)
- File paths to your database
- Any content you enter into the application

BugSnag's own privacy policy is available at https://smartbear.com/privacy/.

## Update Checks (non-Store builds only)

If you installed TermPoint outside the Microsoft Store (e.g. from a direct download), the app periodically checks **GitHub Releases** for newer versions. This check sends a standard HTTPS request to GitHub's API; no personal or institutional data is included. GitHub's privacy policy applies to that request (https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

If you installed TermPoint from the **Microsoft Store**, the Store handles updates and TermPoint makes no update-check requests itself.

## Workload Mailer

The Workload Mailer feature composes emails using your system's default email client (via `mailto:` links). TermPoint **does not send email directly** — it opens a draft in your mail application, and you decide whether to send it. No email content passes through TermPoint's servers or any third-party service.

## What TermPoint Does NOT Do

- **No account or login required** — TermPoint has no user accounts and no authentication system.
- **No cloud sync** — your data stays on the drives you choose.
- **No analytics or telemetry** beyond the crash reporting described above.
- **No advertising or tracking.**
- **No access to contacts, calendar, camera, microphone, or location.**

## Network Access Summary

| Feature | Destination | Data sent | When |
|---|---|---|---|
| Crash reporting | BugSnag (notify.bugsnag.com) | Anonymous error details (see above) | On unhandled exceptions or logged errors |
| Session tracking | BugSnag (sessions.bugsnag.com) | Anonymous session start event | On app launch |
| Update check | GitHub API (api.github.com) | Standard HTTPS request (app version) | Periodically, non-Store builds only |
| Workload Mailer | Local email client | N/A (mailto: URI) | User-initiated only |

## Data Retention

BugSnag retains error data according to its plan terms. On the free tier, error data is retained for 7 days. TermPoint's local data (database and settings) remains on your machine until you delete it.

## Contact

If you have questions about this privacy statement, contact us at gmsschlitt@gmail.com.
