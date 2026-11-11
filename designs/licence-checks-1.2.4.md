# Licence and trial checks, 1.2.4 (summary item 2.2 plus audit fixes)

Approved by Greg 2026-11-11 ("go with the recommendations and wording as drafted"). `MinimumVersionToWrite`: **No**
(nothing new is saved in the database). No save, lock or `.axaml` changes.

## Stories

- **Expiring licence (2.2).** Geography's licence expires July 1. Today Alice sees only the footer date, and on July 2
  TermPoint opens read-only with no warning. **Fixed:** from June 1 every open (and, in a session left open, the first
  check of each new day) shows "Your license expires on July 1, 2027 (30 days from now). …" with "Purchase or renew".
- **Wrong clock at first launch (found in the audit).** Bob's computer clock is a year ahead the first time he opens a
  database. The trial start is saved a year ahead, so once his clock is fixed he sees "395 days remaining". (On
  GregDeskTop, Debug runs with `TERMPOINT_DEBUG_DATE=2027-02-01` did exactly this: `trial=131d` on 22 Oct.) **Fixed:**
  a saved start date in the future is replaced by today, so he gets 30 days from then; never more than 30 are shown.
- **Session left open.** Carol starts her trial, then leaves TermPoint open for six weeks. Today she keeps editing
  indefinitely. **Fixed:** the daily check tells her "Your trial has ended. You can keep working until you close
  TermPoint; next time it opens read-only." She isn't switched to read-only mid-session (that would mean dropping the
  write lock, the risky path).
- **Slow share.** Dave's department is licensed and his trial ended long ago. Over a slow VPN, reading `termpoint.lic`
  takes longer than the 5 s deadline, so today TermPoint falls back to the trial and opens read-only with "Your trial
  has ended". **Fixed:** the last licence file read successfully for that folder is remembered on this computer; when
  the share doesn't answer in time (or the read fails with an error other than "not found"), the remembered file is
  checked again (signature and expiry) and used.

## Changes

1. **`IClock.Today`** (local calendar date). `SystemClock`: `DateOnly.FromDateTime(DateTime.Now)`. `FixedClock`: the
   date of its fixed instant (tests stay time-zone independent). Licence expiry compares against `Today`, so "expires
   July 1" works through the end of July 1 local time (it used to end at midnight UTC, about 5 pm Pacific).
2. **`TrialService`**
   - Parse the start with `CultureInfo.InvariantCulture` + `DateTimeStyles.RoundtripKind`.
   - Saved start later than now → rewrite it as now (log it).
   - `DaysRemaining` never above 30. New `TrialResult.TrialEndsUtc` (start + 30 days).
   - A failed write of `trial.json` is logged and the trial is reported as started now (the open no longer fails).
3. **`LicenseValidator`**
   - Read the file directly: `FileNotFoundException` / `DirectoryNotFoundException` → `NotFound`; any other read error →
     new `LicenseState.Unreadable` (was `Invalid`, which fell back to the trial).
   - `ValidateLicenseText(string)` made public; `LicenseResult.RawContent` carries the text read.
4. **`LicenseCache`** (new, `trial.json`'s folder, `license-cache.json`): folder path (case-insensitive) → last licence
   text read there that was `Licensed` or `Expired`. Saved after such a read; entry removed after a `NotFound`; at most
   20 entries. Every failure is swallowed and logged; a missing or corrupt cache = empty.
5. **`AppLicenseManager`**: `Unreadable`, or a timeout (`EvaluateAccessWithoutLicense(shareDir)`), uses the remembered
   licence if one exists for that folder (re-validated), else the trial as today. `AppAccessResult.TrialEndsUtc` added.
6. **`LicenseNotices`** (new, pure): builds the notice for an `AppAccessResult` and a date, for the open and for the
   daily check. Moves the three notices out of `MainWindow.SetupMainWindowAsync`.
7. **Daily check** (`LicenseWatcher`, hourly timer, acts once per new local date): re-evaluates in the background with
   the network deadline, then posts that day's notice. A timeout posts nothing. Session access is never changed
   mid-session.

## Wording (approved)

At open (unchanged apart from "1 day"):
- Trial: "You have N days remaining in your trial." / "You have 1 day remaining in your trial." Link "Purchase a license".
- Expired: "Your license has expired. Editing is disabled." (not dismissable) Link "Purchase or renew".
- Trial ended: "Your trial has ended. Editing is disabled." (not dismissable) Link "Purchase a license".

New, licence with an expiry date, 0–30 days left (dismissable, link "Purchase or renew"; permanent licences never):
- 2–30: "Your license expires on July 1, 2027 (12 days from now). After that, TermPoint opens read-only: you can view
  your timetable but not edit it."
- 1: "Your license expires tomorrow, July 1, 2027. After that, TermPoint opens read-only: you can view your timetable
  but not edit it."
- 0: "Your license expires at the end of today. After that, TermPoint opens read-only: you can view your timetable but
  not edit it."

New, daily check, when the session has full access but a fresh evaluation says read-only (not dismissable):
- Trial: "Your trial has ended. You can keep working until you close TermPoint; next time it opens read-only." Link
  "Purchase a license".
- Licence: "Your license has expired. You can keep working until you close TermPoint; next time it opens read-only."
  Link "Purchase or renew".

Footer: "Trial: 1 day remaining" (was "1 days").

## Upgrades (point 2 of Greg's question)

Nothing here rewrites `termpoint.lic`. Customers with a normal trial start see no change. Only a start date in the
future is reset (to 30 days from the first open in 1.2.4, never more than they had). Licensed customers see no change
apart from the new notice in their last 30 days and the local-date expiry (a few hours later than before).

## Accepted

Uninstalling deletes the app's data (MSIX), so a reinstall starts a new trial.
