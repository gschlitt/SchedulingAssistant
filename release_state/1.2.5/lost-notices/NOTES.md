# Notices lost behind licence banners: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: none yet (a small design if the banner is restructured).

<details>
<summary>Files to read first</summary>

- `src/TermPoint/Services/AppNotificationService.cs`: one banner slot, a normal queue and a low-priority queue; `EnqueueUnseenAnnouncements` (~line 172) sets `LastAcknowledgedVersion` when it queues, not when shown.
- `src/TermPoint/Licensing/LicenseNotices.cs`: `ForOpen` and `ForDailyCheck`; expired, trial-ended and the "keep working" notices are `IsDismissable = false`.
- `src/TermPoint/MainWindow.axaml.cs` ~lines 735-765: the order notices are enqueued at startup (recovered-lock info, licence notice, then announcements).
- `src/TermPoint/Services/AppNotification.cs`: `IsDismissable`, `AutoDismissAfter`, `IsLowPriority`.

</details>

<details>
<summary>Decisions and why</summary>

- Greg (2026-11-16): add WN1 to 1.2.5 together with a concern that the trial advisory banner may hide other notifications.

</details>

<details>
<summary>Dead ends</summary>

None yet.

</details>

<details>
<summary>Technical issues</summary>

- WN1 (backlog text, seen on TPW10, Store 1.2.3 to 1.2.4 flight, 2026-11-13): announcements are marked seen when queued and are low priority, so a trial countdown at startup hid them; after TermPoint closed they never came back. Expired and trial-ended notices cannot be dismissed, so those users never see them; an expiring licence's notice can hide them too. Licensed departments with no notice are fine. Fix: advance `LastAcknowledgedVersion` only once an announcement has been displayed or dismissed.
- Related, from reading the code only: a normal-priority undismissable notice stays `Current`; later normal notices go to the queue behind it. Not checked whether the trial countdown itself is dismissable, nor whether errors and lock or save warnings use this banner. Reproduce before designing.

</details>
