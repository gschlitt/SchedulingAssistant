using System.Runtime.CompilerServices;

// Allow the test project to access internal members (e.g. WriteLockService.PollLockFile).
[assembly: InternalsVisibleTo("SchedulingAssistant.Tests")]
