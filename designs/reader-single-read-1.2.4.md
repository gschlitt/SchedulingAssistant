# Readers read the shared database once (1.2.4 items 5.3, 5.5, 5.2)

Approved by Greg 2026-11-11 (option A). Reader side only: the save path, the lock and the messages are unchanged.
`MinimumVersionToWrite`: No. Background: `designs/network-experiments-5.8.md` (E2).

## Story

Alice is a reader on a slow VPN and clicks Refresh. Today TermPoint reads the shared database D twice (hash, then
copy), so Bob's autosave can be refused ("in use") during either read: on a Windows server a rename over a file that
someone has open is refused for as long as it is open (E2). **Fixed:** one read that copies and hashes at once, so the
window halves; opening as a reader likewise.

## Why one read is enough

TermPoint only ever replaces D by renaming a finished copy over it; it never writes D in place. A single open handle
therefore reads one whole version: on a Windows server the rename waits until the handle closes (E2 case 1), on Samba
the handle keeps reading the old file (E2 case 3 saw "old" every time). The two-read hash comparison only guarded
against in-place writes by other programs; 5.5's integrity check of the local copy now guards against a damaged copy,
whatever caused it.

## Changes

1. **`NetworkFileOps.CopyAsync` gains a hashing variant** (same stall/deadline handling, same fault seam): it returns
   the SHA-256 of the bytes it copied, computed as they pass (the same hash `ComputeHashAsync` gives for that file).
2. **Refresh (`CheckoutService.RefreshReadOnlySnapshotAsync`):** one hashing copy D → `D''.tmp` instead of hash D +
   copy + compare; the 3-attempt loop for a mid-copy change goes (it can't happen, see above). Before the swap,
   `LocalCopyIntegrity.CheckOnce` on `D''.tmp`: damaged → delete it, keep the old `D''`, outcome `SourceUnavailable`
   (the existing one-line refresh failure), log why. The 0.5–1 s "SMB cache delay" before it goes (5.2).
3. **Reader setup (opening read-only, `CheckoutService` read-only snapshot):** the same: one hashing copy, the integrity
   check, no second read of D. Whatever used the separately read hash now uses the copy's hash.
4. **5.2's two comments** corrected: the save's final-check stamp (size + time can be up to ~10 s old; covered because
   the lock check before it refuses first) and the removed delay.

## Four rules

1. Untouched: a reader never writes D. 2. Untouched: no lock is taken or written. 3. Untouched: a reader has no
unsaved work. 4. Same messages; a damaged copy now gives the plain refresh-failure line instead of SQLite errors later.

## Tests

Hashing copy = `ComputeHashAsync` of the result (small and multi-chunk files; stall and fault paths unchanged).
Refresh with a damaged copy keeps the old `D''` and reports `SourceUnavailable`. Reader setup with a damaged source
fails as an unreadable source does today. Existing refresh, reader-open and fault-seam tests pass; full suite with the
harness.
