# Network shares: what we need to know (1.2.4 item 5.8, reassessing 5.1–5.7)

Draft 2026-11-11, after a review with Greg. Supersedes the E1/E2 wording in `designs/spec_version_1.2.4.md`
("Network shares"). No app or data risk: experiments use dummy files in their own folder on Mekler.

## What changed our view

1. **Windows answers "not found" from a cache for any open, not only for "exists?"** (Microsoft: the not-found cache
   lists files the client *failed to open*; a reproduction saw no network traffic for a failed `OPEN_EXISTING`). So
   "just open the file" (the old 5.1 fix) does not dodge it. `CreateNew` is decided by the server (to be confirmed).
2. **Only failures come from the cache.** A successful open needs a handle from the server, so "the lock exists and
   names X" read by opening is fresh. The 10 s "still exists after a delete" lag affects `File.Exists`, not open+read.
3. **A stale "missing" needs a real "not found" on the same computer within ~5 s.** For the writer, the only plausible
   source is Bob seeing "no lock" just before creating it himself (opening with nobody holding it, his reader poll
   seeing Alice leave, a takeover). That is stale only if Windows keeps the cached "not found" through Bob's own
   create. The October story "Bob's TermPoint deletes and re-creates its own lock" assumed this without asking.
4. **The race between two readers is real and needs no experiment.** Bob and Carol both see Alice leave (a true "not
   found", now cached on both). Carol's `CreateNew` wins; Bob's fails with "already exists"; Bob then asks "exists?",
   gets his cached "not found", and is shown the can't-write message instead of opening read-only. Fix: decide from
   the `CreateNew` error itself (file exists = someone else holds it), not a follow-up "exists?".

## Item by item

| Item | What we need to know | How |
|---|---|---|
| 5.1 lock checks | (a) Does Bob's own create clear his cached "not found"? (b) Does `CreateNew` always reach the server? | **E1** (below). The two-reader race (point 4) is fixed regardless. |
| 5.2 comments | Nothing. Point 2 confirms the 500 ms delay does nothing (the reads after it are opens of an existing file) | Code comments only |
| 5.3 readers block the save | Does a reader opening with "allow replace" let the writer's rename through on a Windows server? What does the reader's open copy see after it? Does a deleted-but-open lock file linger and block the next `CreateNew`? | **E2** (below) |
| 5.4 longer retries | How long renames stay refused in the field, and why | **Field data, not the rig.** 7.1/7.3 (in 1.2.4) report blocked renames with timings. E2 case 1 gives the reader-caused duration. Decide after E2: if 5.3 works, 5.4's remaining causes are antivirus and sync clients, which only the field shows. The cancellation check before each attempt (rule 2) is needed whatever the budget |
| 5.5 refresh integrity check | Nothing | Code only |
| 5.6 server-damaged file | Nothing new; rare | Design choice only |
| 5.7 supported storage | Nothing | Docs only |

## E1: does Bob's own create clear his cached "not found"? (5.1)

One computer (GregDeskTop) against Mekler; about 5 minutes. Per run (20 runs, a new file name each time, 11 s apart):
check `X.lock` (gets "not found" from the server: this primes the cache) → `CreateNew` it → immediately check again
with `File.Exists`, then with open+read → delete it. Variants: prime with `File.Exists` and with a failed open.

Then (b): prime "not found", let **TPVM1** create the file, and have GregDeskTop try `CreateNew` at once (20 runs):
it must fail with "already exists" every time. Also time how long GregDeskTop's `File.Exists` and open keep saying
"not found" after TPVM1's create (expect up to ~5 s): this confirms point 1 on our server.

| Result | 5.1 becomes |
|---|---|
| (a) second check never says "missing"; (b) `CreateNew` always fails | Writer story closed. 5.1 = the two-reader fix (point 4) + tidy the exists/read race. Small |
| (a) second check sometimes says "missing" | Before acting on "my lock is missing", the writer confirms with `CreateNew`: "already exists" = stale, ignore. Check this against the rules and the takeover first |
| (b) `CreateNew` ever succeeds over an existing file | Serious (lock not exclusive): stop and investigate before anything else |

## E2: does a reader's open file block the writer's save? (5.3, informs 5.4)

TPVM1 holds the file open; GregDeskTop renames a temp file over it exactly as a save does (`File.Move`, overwrite).
5 runs per case; an 8 MB file; about 10 minutes.

| # | Reader's open | Checks |
|---|---|---|
| 1 | As TermPoint today (`ReadWrite`) | Control: "in use" expected. How long it stays refused (retry every 100 ms) |
| 2 | `ReadWrite \| Delete` | Does the rename go through? |
| 3 | As 2, reading during the rename | After it, does the reader's handle give the old content, an error, or a mix (compare hashes)? |
| 4 | Lock file, `ReadWrite \| Delete`, while the writer's heartbeat replaces it | As 2 and 3 for the lock |
| 5 | Lock file, `ReadWrite \| Delete`, while the writer deletes it (release) | Does a third `CreateNew` fail while the reader's handle is open ("delete pending")? |

| Result | Then |
|---|---|
| 2 passes, 3 never mixes, 5 doesn't block | 5.3 as planned for the database and the lock file |
| 2 passes but 5 blocks | "Allow replace" for the database only; lock reads unchanged |
| 2 refused | 5.3 doesn't help on Windows servers; readers should hold the file for less time (copy fast, close), and 5.4 carries the rest |

## Build and run

- New NetworkProbe mode, two roles (runner on GregDeskTop, partner on TPVM1 over a small TCP control channel, so one
  clock times everything). Dry run against a local folder first, like the other modes. Sonnet builds it.
- Record both computers' SMB client cache settings (`Get-SmbClientConfiguration`) in the report. If E1 is ambiguous,
  repeat it with `FileNotFoundCacheLifetime 0` on the rig as a control (never something to ask of customers).
- Limits: Mekler is Windows (no Samba box); macOS's SMB client is not covered; TPVM1 reaches Mekler through Hyper-V's
  Default Switch (NAT), which should not affect caching.
- Greg's time: about 20 minutes on the rig, mostly unattended.

## Results (2026-11-11, GregDeskTop + TPVM1 against Mekler; report `tmp/netprobe/netprobe-20261111-120619-share-semantics.md`, local only)

SMB client settings were Windows defaults (not-found 5 s, file info 10 s, directory 10 s; the report failed to read
them, a probe bug since fixed; GregDeskTop checked by hand).

- **E1a: 20 of 20 never "missing".** Bob's own create clears his cached "not found". **E1b: `CreateNew` was refused
  "already exists" 20 of 20** (decided by the server). A plain open stayed "not found" for 4.9–5.1 s after another
  computer's create, exactly like `File.Exists`: point 1 confirmed. **Row 1 applies:** the writer story is closed;
  5.1 = the two-reader fix (decide from `CreateNew`'s own error) + tidy the exists/read race. Small, no rule risk.
- **E2: "allow replace" does not help on a Windows server.** Case 1: refused for as long as the reader holds the file,
  through after it closes (0.01 s). Cases 2–4: refused 5 of 5 even with `ReadWrite|Delete`, for the database and the
  lock file; the reader kept reading the old content (no mix). **Row 3 applies.**
- **Case 5 is a hazard to avoid:** a lock file deleted while someone holds it open with `Delete` sharing stays
  "delete pending", and **every `CreateNew` is refused (access denied) until that handle closes**. Never add `Delete`
  sharing to TermPoint's lock-file reads. (Today's reads, without it, make the release's delete fail instead, which is
  already retried.)

**What this means for the other items**
- **5.3** becomes "readers hold D for less time": a refresh hashes D and then copies it (two full reads); one pass
  that copies and hashes together halves the time a save can be refused. Check every other reader read of D the same
  way. Design and risk review first.
- **5.4** is still needed (readers on a slow link, antivirus, sync clients): the refusal lasts exactly as long as the
  other side holds the file, so the retry budget should cover a typical reader copy. Size it from the 7.1/7.3 field
  reports and the reader copy times; cancellation check before every attempt (rule 2).
