using System.Runtime.CompilerServices;

// Allow the test project to access internal members (e.g. WriteLockService.PollLockFile).
[assembly: InternalsVisibleTo("TermPoint.Tests")]

// tools/NetworkProbe drives the real backup rotation (BackupService.RotateBackupsAsync) against a share it can cut.
[assembly: InternalsVisibleTo("NetworkProbe")]
