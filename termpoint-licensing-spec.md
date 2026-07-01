# TermPoint Department Licensing — Design Spec

## Context

TermPoint is a desktop timetabling app (C#/Avalonia, .NET) distributed via the Microsoft Store (free) and potentially via direct download (Velopack). It uses a local-first architecture: a shared SQLite database on a network drive, with a `.tpconfig` file alongside it. There are no servers, no accounts, no telemetry.

The licensing model must match this philosophy. A license belongs to a **department position**, not a person. Staff rotate; the license stays. Anyone who can access the shared drive is authorized.

## Architecture Overview

```
┌─────────────────────┐
│  Paddle (purchase)   │──webhook──▶ Key generation function
└─────────────────────┘            (Azure Function / Cloudflare Worker)
                                            │
                                    signs blob with private key
                                            │
                                            ▼
                                   emails key to buyer
                                            │
                                            ▼
                              buyer saves key file to network share
                                            │
                                            ▼
┌─────────────────────────────────────────────────────┐
│  Network Share (S:\Scheduling\)                     │
│                                                     │
│   database.tp                                       │
│   .tpconfig                                         │
│   termpoint.lic    ◀── license key file lives here  │
└─────────────────────────────────────────────────────┘
                                            │
                                    on launch, TermPoint reads
                                    and validates with embedded
                                    public key
```

## Phase 1 — License Key Format

### Payload structure

The license key is a JSON payload, signed and base64-encoded.

```json
{
  "department": "Arizona State Psychology Dept",
  "issued": "2026-07-01",
  "expiry": "2027-07-01",
  "version": 1
}
```

Field rules:
- `department` (string, required): Display name. Shown in the app UI ("Licensed to: ..."). Free-text, entered at key generation time.
- `issued` (string, required): ISO 8601 date. The date the key was generated.
- `expiry` (string | null, required): ISO 8601 date or null. Null means never expires (used for free/permanent keys — early adopters, coupons, demos).
- `version` (int, required): Schema version. Start at 1. Allows future changes to the payload format without breaking old keys.

### Signing mechanism

- Algorithm: RSA with SHA-256 (RSA is well-supported in .NET's `System.Security.Cryptography`).
- Generate a 2048-bit RSA keypair.
- Private key: stored securely, used only by the key generation service and for manual key generation on Greg's dev machine. Never shipped in the app.
- Public key: embedded in the TermPoint binary as a resource or constant.

### Key file format

The file `termpoint.lic` contains two sections separated by a blank line:

```
<base64-encoded JSON payload>

<base64-encoded RSA signature of the payload bytes>
```

This keeps the format human-inspectable (you can decode the top half to see what it says) and simple to parse.

### Why this approach

- No phone-home required. Validation is pure local crypto.
- No infrastructure dependency. Keys can be generated offline with a script.
- Forgery requires the private key. Good enough for a $40 product in a niche market.

## Phase 2 — Client-Side Validation in TermPoint

### License file location

TermPoint looks for `termpoint.lic` in the **same directory as the shared database** (the network share root where `.tpconfig` lives). This is critical: the license travels with the data, not with the app installation or the user profile.

The path to the share is already known to TermPoint (it's how the app finds the database). No additional configuration needed.

### Validation logic

On launch (and periodically if desired, but launch is sufficient):

```
1. Look for termpoint.lic at the share path.
2. If not found → UNLICENSED state.
3. If found:
   a. Read the payload and signature.
   b. Verify the signature against the embedded public key.
   c. If signature invalid → UNLICENSED state (treat as if no file; do not surface scary "tampered" errors to non-technical users).
   d. Parse the JSON payload.
   e. Check `version` field — if higher than what this build understands, warn but do not reject (forward compatibility).
   f. Check `expiry`:
      - If null → LICENSED state (permanent).
      - If in the future → LICENSED state.
      - If in the past → EXPIRED state.
```

### App states

The app has three mutually exclusive states. The state is determined by combining license validation with trial status.

| State | Condition | Behaviour |
|-------|-----------|-----------|
| **LICENSED** | Valid, non-expired `termpoint.lic` found on share | Full access. Show "Licensed to: {department}" somewhere unobtrusive (About screen, status bar, or similar). |
| **TRIAL** | No valid license found, but trial period has not elapsed | Full access. Show trial days remaining. Show clear path to purchase (link to termpoint.ca/buy). |
| **UNLICENSED** | No valid license found and trial has elapsed, OR license is expired | Restricted access (see below). Show clear path to purchase and "Enter license key" option. |

### Restricted access (UNLICENSED state)

Design decision for Greg: what does "restricted" mean? Options from softest to hardest:

- **Read-only**: can view the timetable but not edit. Good for maintaining visibility while nudging purchase.
- **Nag screen on launch**: full access but a modal every launch. Annoying but not blocking.
- **Full lock**: can't proceed past a license screen. Hardest gate.

**Recommendation**: Read-only. It respects the department's existing data (they can still see their timetable), doesn't create emergencies ("the scheduling tool is locked and classes start Monday"), and the friction of not being able to edit is enough motivation to buy. An "Enter license key" button on the read-only banner gives them a clear path forward.

### Separation of concerns

License validation should be implemented as a standalone service/class with no UI dependencies:

```
ILicenseValidator
  - ValidateLicenseFile(string shareDirectoryPath) → LicenseResult
  
LicenseResult
  - State: Licensed | Expired | Invalid | NotFound
  - Department: string?
  - Expiry: DateTime?
  - ErrorReason: string? (for logging, not user display)
```

This makes it independently testable without any Avalonia UI involvement.

## Phase 3 — Trial Clock

### Storage

Trial start date is recorded **per-installation, per-user**, in the user's local AppData folder (not on the share). Rationale:

- Trial state is individual, not departmental. If Nancy trials it and lets it expire, that shouldn't burn the trial for Jordan when Jordan starts next month.
- Storing it on the share would let one expired trial block everyone.

Location: `%APPDATA%\TermPoint\trial.json` (or equivalent via `Environment.GetFolderPath`).

```json
{
  "trialStartedUtc": "2026-07-01T14:30:00Z",
  "version": 1
}
```

### Trial logic

```
1. On launch, after license validation returns NotFound or Invalid:
2. Read trial.json from AppData.
3. If not found → first launch. Create trial.json with current UTC timestamp. State = TRIAL (30 days remaining).
4. If found:
   a. Compute elapsed days since trialStartedUtc.
   b. If < 30 → State = TRIAL. Show (30 - elapsed) days remaining.
   c. If >= 30 → State = UNLICENSED.
```

### Tampering resistance

Keep it simple. This is a $40/year product in a narrow institutional market. The person who would delete trial.json to reset their trial is not your customer and never will be.

Do NOT invest in registry keys, hardware fingerprinting, obfuscation, or phone-home checks. If this ever becomes a real problem (it won't), address it then.

### Trial service interface

```
ITrialService
  - GetTrialStatus() → TrialResult

TrialResult
  - IsInTrial: bool
  - DaysRemaining: int
  - TrialExpired: bool
```

Again, standalone, no UI dependencies, independently testable.

## Phase 4 — Composite App State

A single orchestrator combines license and trial status:

```
IAppLicenseManager
  - EvaluateAccess(string shareDirectoryPath) → AppAccessResult

AppAccessResult
  - AccessLevel: FullAccess | ReadOnly
  - Reason: Licensed | Trial | Unlicensed | Expired
  - DepartmentName: string?       (if licensed)
  - DaysRemaining: int?           (if trial)
  - ExpiryDate: DateTime?         (if licensed with expiry)
  - ShowPurchasePrompt: bool
```

Logic:

```
1. Run license validation.
2. If Licensed → FullAccess, reason=Licensed.
3. If Expired → ReadOnly, reason=Expired, ShowPurchasePrompt=true.
4. If NotFound/Invalid → run trial check.
   a. If in trial → FullAccess, reason=Trial, ShowPurchasePrompt=true (soft).
   b. If trial expired → ReadOnly, reason=Unlicensed, ShowPurchasePrompt=true.
```

## Phase 5 — Key Generation Tooling

### CLI tool (for Greg's manual use)

A simple .NET console app that:

1. Takes department name and expiry date (or "permanent") as arguments.
2. Loads the private key from a local file.
3. Produces a `termpoint.lic` file.

Example usage:

```
dotnet run -- --department "UBC Geography" --expiry 2027-07-01 --output termpoint.lic
dotnet run -- --department "UBC Geography" --permanent --output termpoint.lic
```

This is essential for:
- Generating keys for existing departments already using TermPoint.
- Testing during development.
- Emergency key issuance without depending on the webhook pipeline.

### Keypair generation script

A one-time script (can be part of the CLI tool) that generates the RSA keypair and outputs:
- `termpoint-private.pem` — keep secure, never commit to repo.
- `termpoint-public.pem` — embed in the app.

### Automated generation (Paddle webhook, later)

A small serverless function that receives a Paddle webhook, extracts the buyer's info, generates a signed key, and emails it. This is Phase 5 of implementation but can be deferred — manual key generation is fine for early sales.

## Phase 6 — In-App UX

### "Enter License Key" flow

A button accessible from:
- The trial countdown banner.
- The read-only restriction banner.
- The About screen.

Clicking it opens a file picker dialog defaulting to the share directory path. User selects or pastes their `termpoint.lic` file, and TermPoint copies it to the share root (next to `.tpconfig`). Revalidation happens immediately.

Alternative: a text box where they paste the key contents directly, and TermPoint writes the file. Either works; file picker is probably more intuitive for the target audience.

**Important**: the key file must be written to the share, not to a local path. This is what makes the license departmental rather than personal.

### Purchase prompt

When `ShowPurchasePrompt` is true, display (in a non-blocking way):

- Trial: "You have X days remaining in your trial. Purchase a department license at termpoint.ca/buy"
- Unlicensed/expired: "[Read-only mode] Your trial has ended. Purchase a department license to continue editing. [Buy License] [Enter License Key]"

The "Buy License" button opens the default browser to termpoint.ca/buy.

### "Licensed to" display

When licensed, show the department name somewhere low-key. About screen is the minimum. A subtle status bar indicator is nice but not required for v1.

## Testing Strategy

This is a critical section. The licensing system must work reliably across unknown university environments with varying security settings, network configurations, and IT policies.

### Unit tests (no file system, no crypto)

- Payload parsing: valid JSON, missing fields, unknown version, null expiry, past expiry, future expiry.
- Trial date math: day 0, day 1, day 29, day 30, day 31, edge cases around midnight/timezone boundaries.
- Composite state logic: all combinations of license state × trial state → correct AppAccessResult.

### Integration tests (file system + crypto, no UI)

- Keypair generation → sign payload → validate signature round-trip.
- Valid key file on disk → LICENSED.
- Corrupt key file → UNLICENSED (no crash, no scary error).
- Missing key file → triggers trial path.
- Key with past expiry → EXPIRED.
- Key with null expiry → LICENSED (permanent).
- Key signed with wrong private key → UNLICENSED.
- Garbage file named termpoint.lic → UNLICENSED (no crash).
- Empty file → UNLICENSED (no crash).
- Read-only file system (user can read but not write to share) → validation still works (read-only access to the .lic file is sufficient).

### Simulated environment tests

- Key file on a mapped network drive (test with an SMB share, even a local one created via `net share`).
- Key file on a UNC path (`\\server\share\termpoint.lic`).
- Key file where the share is temporarily unavailable (offline, VPN disconnected) — TermPoint should degrade gracefully, not crash. Consider: should it cache the last-known license state locally for brief outages? If so, cache in AppData with a short TTL (e.g., 24 hours).

### Manual / exploratory tests

- Full lifecycle: install from Store → first launch (trial starts) → use for "30 days" (advance system clock or use a debug override) → trial expires → go read-only → enter license key → full access.
- Staff transition: Nancy has TermPoint installed and licensed. Jordan installs TermPoint fresh, points to same share, immediately gets LICENSED state with no trial needed.
- License renewal: old key expires, new key replaces the file on the share, app picks up new expiry on next launch.

### Debug / development affordances

- A launch flag or debug setting that overrides the current date for trial/expiry testing (e.g., `--override-date 2027-08-01`). Never ship this enabled; strip it or gate it behind a `#if DEBUG` flag.
- Logging: license validation and trial evaluation should log their decisions (file found/not found, signature valid/invalid, days remaining, final state) to TermPoint's existing log infrastructure. No sensitive data in logs (don't log the full key contents).

## Security Considerations

- **Private key storage**: never in the repo, never in the app binary. Store it in a secure location on Greg's dev machine. If using a serverless function for automated generation, store it in the function's secret/environment config (Azure Key Vault, Cloudflare secret, etc.).
- **Public key embedding**: safe to ship in the binary. It only allows verification, not forgery.
- **Key file permissions**: TermPoint only needs read access to `termpoint.lic`. It needs write access only during the "Enter License Key" flow (writing the file to the share). If write fails (permissions), prompt the user to copy the file manually.
- **Clock manipulation**: a user could set their system clock back to extend a trial or un-expire a license. This is fine. See "tampering resistance" above — not worth defending against for this product and market.

## What This Spec Does NOT Cover

- **Paddle account setup and configuration** — administrative task, not a code design problem.
- **termpoint.ca/buy page design** — marketing/web work, separate from the C# codebase.
- **Webhook-based key generation function** — deferred; manual CLI key generation is sufficient for launch. Spec this separately when ready.
- **Store listing changes** — administrative task, do after the code is working.
- **Velopack direct-download distribution** — independent of licensing, can be added later.

## Implementation Order

1. **Keypair generation + CLI key generator** — unblocks everything else.
2. **ILicenseValidator + tests** — core crypto validation, no UI.
3. **ITrialService + tests** — trial clock logic, no UI.
4. **IAppLicenseManager + tests** — composite state, no UI.
5. **Wire into TermPoint UI** — banners, About screen, "Enter License Key" flow.
6. **Simulated environment testing** — SMB shares, UNC paths, offline scenarios.
7. **Manual lifecycle testing** — full end-to-end walkthrough.
8. **Paddle setup + termpoint.ca/buy** — can happen in parallel with steps 2–4.
9. **Store listing update** — last step, after everything works.
