<#
.SYNOPSIS
    TermPoint field-test toolkit: reset and inspect the shared TEST database, drive TermPoint on several computers
    (this one, Hyper-V VMs, WinRM machines), freeze VMs, and check the app's logs against scenario files.

.DESCRIPTION
    Import with:  Import-Module .\FieldTest.psm1 -Force
    Everything machine-specific (share path, user, machine names) comes from fieldtest.config.psd1 beside this file
    (or the file named by $env:FIELDTEST_CONFIG); see fieldtest.config.example.psd1.

    Works under Windows PowerShell 5.1 and PowerShell 7. Remote script blocks never use $using:; values travel via
    param() and -ArgumentList. The module never opens any database on a share with SQLite: it copies files, renames
    files and runs python helpers on LOCAL copies only.

    Rules this module follows:
      * Commands that need to know that TermPoint is closed everywhere (Reset-TestDatabase, Set-TestLock -Kind Clear)
        check EVERY configured machine and refuse when they cannot be sure.
      * A configured VM that Get-VM cannot find is "not created": skipped by all-machines checks, an error when
        named directly.
      * Read-only commands (Get-TestDatabaseState, Watch-TestLock, Test-TermPointLog on Local roles) work from a
        non-elevated console.
#>

# Cached PSSessions and credentials, per console (module scope).
$script:Sessions    = @{}
$script:Credentials = @{}

# C# used by Suspend-TermPoint / Resume-TermPoint (NtSuspendProcess / NtResumeProcess). Passed to the target session
# as an argument, so no $using: is needed.
$script:NtSource = @'
using System;
using System.Runtime.InteropServices;
public static class FieldTestNt {
    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static int Apply(int pid, bool resume) {
        IntPtr h = OpenProcess(0x0800, false, pid);   // PROCESS_SUSPEND_RESUME
        if (h == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { return resume ? NtResumeProcess(h) : NtSuspendProcess(h); } finally { CloseHandle(h); }
    }
}
'@

# C# used by Edit-TestDatabaseAsColleague: one rename that replaces D (MoveFileEx with MOVEFILE_REPLACE_EXISTING),
# because Windows PowerShell 5.1's Move-Item -Force deletes the destination first and then moves (two steps, so a
# TermPoint poll in between would see D missing).
$script:MoveSource = @'
using System;
using System.Runtime.InteropServices;
public static class FieldTestFile {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool MoveFileEx(string existing, string replacement, uint flags);
    public static void ReplaceByRename(string source, string destination) {
        // MOVEFILE_REPLACE_EXISTING (0x1) | MOVEFILE_WRITE_THROUGH (0x8)
        if (!MoveFileEx(source, destination, 0x1 | 0x8))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
'@

#region ---------------------------------------------------------------- configuration and machines

<#
.SYNOPSIS
    Loads and validates the field-test configuration.
.DESCRIPTION
    Reads $env:FIELDTEST_CONFIG if set, else fieldtest.config.psd1 beside the module, with Import-PowerShellDataFile.
    Fills in defaults for optional settings. Read again on every call, so edits take effect at once.
.OUTPUTS
    [hashtable] the configuration.
.EXAMPLE
    (Get-TestConfig).Database
.NOTES
    Throws when the file is missing (naming the example file) or a required setting is absent.
#>
function Get-TestConfig {
    [CmdletBinding()]
    param()
    $path = $env:FIELDTEST_CONFIG
    if (-not $path) { $path = Join-Path $PSScriptRoot 'fieldtest.config.psd1' }
    if (-not (Test-Path -LiteralPath $path)) {
        throw ("Field-test config not found: $path . Copy fieldtest.config.example.psd1 (beside the module) to " +
               "fieldtest.config.psd1 and edit it, or set FIELDTEST_CONFIG to your file.")
    }
    $c = Import-PowerShellDataFile -LiteralPath $path
    foreach ($k in 'Database', 'Master', 'WorkFolder', 'Machines') {
        if (-not $c.ContainsKey($k) -or -not $c[$k]) { throw "Config $path is missing the setting '$k'." }
    }
    $defaults = @{ MasterHash = ''; ShareRoot = ''; ShareUser = ''; PythonExe = 'py'; PythonArgs = @('-3.11', '-I');
                   MsixPackageName = 'AcademicSolutions.TermPoint'; ExePath = '' }
    foreach ($k in $defaults.Keys) { if (-not $c.ContainsKey($k) -or $null -eq $c[$k]) { $c[$k] = $defaults[$k] } }
    $c['ConfigPath'] = $path
    return $c
}

<#
.SYNOPSIS
    Returns one configured machine, validated.
.PARAMETER Name
    The machine's Name from the config.
.OUTPUTS
    [hashtable] with Name, Kind (Local|VM|WinRM), Enabled, VMName / ComputerName defaulted.
.NOTES
    Throws, listing the valid names, when the name is unknown or the kind is invalid.
#>
function Get-TestMachine {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    $cfg = Get-TestConfig
    $all = @($cfg.Machines)
    $m = $all | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
    if (-not $m) { throw "Unknown machine '$Name'. Valid names: $((($all | ForEach-Object { $_.Name }) -join ', '))." }
    if ('Local', 'VM', 'WinRM' -notcontains $m.Kind) { throw "Machine '$Name' has invalid Kind '$($m.Kind)' (Local, VM or WinRM)." }
    if (-not $m.ContainsKey('Enabled')) { $m['Enabled'] = $true }
    if ($m.Kind -eq 'VM' -and -not $m.VMName) { $m['VMName'] = $m.Name }
    if ($m.Kind -eq 'WinRM' -and -not $m.ComputerName) { $m['ComputerName'] = $m.Name }
    return $m
}

<# Returns $true when this console is elevated. #>
function Test-ElevatedConsole {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

<# Throws unless this console is elevated; $Message is the error text. #>
function Assert-ElevatedConsole {
    param([string]$Message = 'Run this console as administrator (Hyper-V and PowerShell Direct need it).')
    if (-not (Test-ElevatedConsole)) { throw $Message }
}

<#
.SYNOPSIS
    The Hyper-V state of a VM machine: 'Running', 'Off', 'Paused', 'Saved', ... or 'NotCreated'.
.PARAMETER Machine
    A machine hashtable from Get-TestMachine (Kind VM).
.NOTES
    Throws when the console is not elevated or the Hyper-V module is missing.
#>
function Get-TestVmState {
    param([Parameter(Mandatory)][hashtable]$Machine)
    Assert-ElevatedConsole -Message 'Run this console as administrator so the VMs can be checked.'
    if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw 'The Hyper-V PowerShell module is not installed on this computer.' }
    $vm = Get-VM -Name $Machine.VMName -ErrorAction SilentlyContinue
    if (-not $vm) { return 'NotCreated' }
    return [string]$vm.State
}

<# Returns the VM machine named $Name after checking it is a VM, the console is elevated and the VM exists. #>
function Get-CreatedVmMachine {
    param([Parameter(Mandatory)][string]$Name)
    $m = Get-TestMachine -Name $Name
    if ($m.Kind -ne 'VM') { throw "Machine '$Name' is not a VM (Kind=$($m.Kind))." }
    $state = Get-TestVmState -Machine $m
    if ($state -eq 'NotCreated') { throw "VM '$($m.VMName)' has not been created yet (Get-VM cannot find it)." }
    return $m
}

<#
.SYNOPSIS
    Lists the machines an all-machines command should look at.
.DESCRIPTION
    Local always; VM when it is Running; WinRM when Enabled. Off and not-created VMs are skipped.
.PARAMETER Strict
    For safety checks. Throws when the console is not elevated and the config has any VM; for a VM that is
    Paused/Saved (TermPoint might be alive inside it) or in a transitional state.
    Without -Strict (read-only listings) VMs are skipped with a warning when the console is not elevated.
.OUTPUTS
    Machine hashtables.
#>
function Get-ActiveTestMachine {
    [CmdletBinding()]
    param([switch]$Strict)
    $cfg = Get-TestConfig
    $result = New-Object System.Collections.Generic.List[object]
    $haveVm = @($cfg.Machines | Where-Object { $_.Kind -eq 'VM' }).Count -gt 0
    $elevated = Test-ElevatedConsole
    if ($Strict -and $haveVm -and -not $elevated) { throw 'Run this console as administrator so the VMs can be checked.' }
    foreach ($raw in @($cfg.Machines)) {
        $m = Get-TestMachine -Name $raw.Name
        switch ($m.Kind) {
            'Local' { $result.Add($m) }
            'WinRM' { if ($m.Enabled) { $result.Add($m) } }
            'VM' {
                if (-not $elevated) { Write-Warning "Skipping VM '$($m.Name)': console is not elevated."; continue }
                $state = Get-TestVmState -Machine $m
                if ($state -eq 'Running') { $result.Add($m) }
                elseif ($state -eq 'Off' -or $state -eq 'NotCreated') { }
                elseif ($Strict -and ($state -eq 'Paused' -or $state -eq 'Saved')) {
                    throw "$($m.Name) is paused/saved with TermPoint possibly running; resume it and close TermPoint, or restore its checkpoint."
                }
                elseif ($Strict) { throw "VM '$($m.Name)' is in state '$state'; wait until it is Running or Off, then retry." }
            }
        }
    }
    return $result.ToArray()
}

<# Throws unless the shared test database file D exists and can be seen from this console. #>
function Assert-DatabaseVisible {
    param([Parameter(Mandatory)][hashtable]$Config)
    if (-not (Test-Path -LiteralPath $Config.Database)) {
        throw "Can't see the test database from this window. In an elevated window run Connect-TestShare first."
    }
}

<#
.SYNOPSIS
    Computes the names of D's companion files from the config.
.OUTPUTS
    [pscustomobject] Db, Dir, Name, Lock, LockTmp, DbTmp, Wal, Shm (full paths) and NameNoExt, Ext.
#>
function Get-TestDatabasePath {
    param([Parameter(Mandatory)][hashtable]$Config)
    $d = $Config.Database
    $dir = Split-Path -Parent $d
    $name = Split-Path -Leaf $d
    $noExt = [IO.Path]::GetFileNameWithoutExtension($name)
    $lock = Join-Path $dir ($noExt + '.lock')
    [pscustomobject]@{
        Db = $d; Dir = $dir; Name = $name; NameNoExt = $noExt; Ext = [IO.Path]::GetExtension($name)
        Lock = $lock; LockTmp = $lock + '.tmp'; DbTmp = $d + '.tmp'; Wal = $d + '-wal'; Shm = $d + '-shm'
    }
}

<# Creates a folder (and parents) if needed and returns its path. #>
function Initialize-Folder {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Force -Path $Path | Out-Null }
    return $Path
}

<# SHA-256 of a file as an uppercase hex string. #>
function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

<# First 8 characters of a hash, or '-' when empty. #>
function Get-ShortHash {
    param([string]$Hash)
    if (-not $Hash) { return '-' }
    return $Hash.Substring(0, [Math]::Min(8, $Hash.Length))
}

<# The time of the last Reset-TestDatabase, or $null. #>
function Get-LastResetTime {
    $cfg = Get-TestConfig
    $f = Join-Path $cfg.WorkFolder 'last-reset.txt'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    $text = (Get-Content -LiteralPath $f -Encoding ASCII | Select-Object -First 1)
    $t = [datetime]::MinValue
    if ([datetime]::TryParse($text, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$t)) { return $t }
    return $null
}

<# The default -Since for log commands: last reset, else one hour ago. #>
function Get-DefaultSince {
    $t = Get-LastResetTime
    if ($t) { return $t }
    return (Get-Date).AddHours(-1)
}

<# Writes a line prefixed with the local time. #>
function Write-Step {
    param([string]$Text)
    Write-Host ("{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text)
}

#endregion

#region ---------------------------------------------------------------- core plumbing

<#
.SYNOPSIS
    Returns the cached PSSession for a machine, creating it when needed (Local returns $null).
.DESCRIPTION
    Asks for credentials once per console (Get-Credential). With -SaveCredential the credential is written with
    Export-Clixml to "$env:LOCALAPPDATA\TermPointFieldTest\<name>.cred.xml" (DPAPI: this user on this computer only) and
    loaded from there next time. A cached session is reused while its State is Opened.
.PARAMETER Name
    Machine name from the config.
.PARAMETER SaveCredential
    Save the credential for later consoles.
.OUTPUTS
    [System.Management.Automation.Runtime.PSSession] or $null for Local.
.EXAMPLE
    Connect-TestComputer -Name TPVM1 -SaveCredential
.NOTES
    Throws when not elevated (VM), when the VM does not exist, when a WinRM machine is disabled, or the connection fails.
#>
function Connect-TestComputer {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name, [switch]$SaveCredential)
    $m = Get-TestMachine -Name $Name
    if ($m.Kind -eq 'Local') { return $null }
    if ($m.Kind -eq 'WinRM' -and -not $m.Enabled) { throw "Machine '$($m.Name)' is disabled in the config (Enabled = `$false)." }
    if ($m.Kind -eq 'VM') {
        Assert-ElevatedConsole
        $state = Get-TestVmState -Machine $m
        if ($state -eq 'NotCreated') { throw "VM '$($m.VMName)' has not been created yet (Get-VM cannot find it)." }
        if ($state -ne 'Running') { throw "VM '$($m.VMName)' is $state, not Running." }
    }
    $s = $script:Sessions[$m.Name]
    if ($s -and $s.State -eq 'Opened') { return $s }
    if ($s) { Remove-PSSession -Session $s -ErrorAction SilentlyContinue; $script:Sessions.Remove($m.Name) }

    $cred = Get-TestCredential -Name $m.Name -Save:$SaveCredential
    if ($m.Kind -eq 'VM') { $s = New-PSSession -VMName $m.VMName -Credential $cred -ErrorAction Stop }
    else { $s = New-PSSession -ComputerName $m.ComputerName -Credential $cred -ErrorAction Stop }
    $script:Sessions[$m.Name] = $s
    return $s
}

<# Returns the credential for a machine: cached, else saved file, else a prompt. Optionally saves it. #>
function Get-TestCredential {
    param([Parameter(Mandatory)][string]$Name, [switch]$Save)
    $file = Join-Path (Join-Path $env:LOCALAPPDATA 'TermPointFieldTest') ($Name + '.cred.xml')
    $cred = $script:Credentials[$Name]
    if (-not $cred) {
        if (Test-Path -LiteralPath $file) { $cred = Import-Clixml -LiteralPath $file }
        else {
            $cred = Get-Credential -Message "Sign-in for field-test machine $Name"
            if (-not $cred) { throw "No credential given for $Name." }
        }
        $script:Credentials[$Name] = $cred
    }
    if ($Save) {
        Initialize-Folder -Path (Split-Path -Parent $file) | Out-Null
        $cred | Export-Clixml -LiteralPath $file
    }
    return $cred
}

<# True when a session is cached or a saved credential exists for the machine (so connecting will not prompt). #>
function Test-CanConnectQuietly {
    param([Parameter(Mandatory)][string]$Name)
    $s = $script:Sessions[$Name]
    if ($s -and $s.State -eq 'Opened') { return $true }
    if ($script:Credentials[$Name]) { return $true }
    return (Test-Path -LiteralPath (Join-Path (Join-Path $env:LOCALAPPDATA 'TermPointFieldTest') ($Name + '.cred.xml')))
}

<# Forgets (and closes) a machine's cached session, e.g. after the VM was paused or restored. #>
function Remove-CachedSession {
    param([Parameter(Mandatory)][string]$Name)
    $s = $script:Sessions[$Name]
    if ($s) { Remove-PSSession -Session $s -ErrorAction SilentlyContinue; $script:Sessions.Remove($Name) }
}

<#
.SYNOPSIS
    Runs a script block on a machine.
.DESCRIPTION
    Local: runs in-process. Others: Invoke-Command in the machine's session (Windows PowerShell 5.1 there). Never use
    $using: in the block; pass values with param() and -ArgumentList.
.PARAMETER Name
    Machine name.
.PARAMETER ScriptBlock
    The code.
.PARAMETER ArgumentList
    Positional arguments for the block's param().
.OUTPUTS
    Whatever the block outputs (deserialized when remote).
.EXAMPLE
    Invoke-TestComputer -Name TPVM1 -ScriptBlock { param($x) $x * 2 } -ArgumentList 21
#>
function Invoke-TestComputer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$ScriptBlock,
        [object[]]$ArgumentList = @()
    )
    $m = Get-TestMachine -Name $Name
    if ($m.Kind -eq 'Local') { return (& $ScriptBlock @ArgumentList) }
    $s = Connect-TestComputer -Name $Name
    return (Invoke-Command -Session $s -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList)
}

<# Copies one file from a machine to a local path (Copy-Item -FromSession when remote). #>
function Copy-FromTestComputer {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Destination)
    $s = Connect-TestComputer -Name $Name
    Initialize-Folder -Path (Split-Path -Parent $Destination) | Out-Null
    if ($s) { Copy-Item -FromSession $s -LiteralPath $Path -Destination $Destination -Force }
    else { Copy-Item -LiteralPath $Path -Destination $Destination -Force }
}

<# Base64 of a script in the form powershell.exe -EncodedCommand wants (avoids all quoting problems). #>
function ConvertTo-EncodedCommand {
    param([Parameter(Mandatory)][string]$Script)
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script))
}

<#
.SYNOPSIS
    Internal: runs a program in the signed-in user's desktop session on a machine.
.DESCRIPTION
    Remote sessions have no desktop, so on VM/WinRM machines a one-shot scheduled task is registered for the
    signed-in console user (principal LogonType Interactive; RunLevel Highest with -Highest), started, optionally
    waited for, then unregistered. Task names start with 'TermPointFieldTest-'. Local: Start-Process directly.
.PARAMETER Name
    Machine name.
.PARAMETER Execute
    Program to run.
.PARAMETER Argument
    Its command line.
.PARAMETER Highest
    Run elevated (remote only).
.PARAMETER WaitSeconds
    Wait up to this long for it to finish (0 = a short grace period only).
.OUTPUTS
    The program's exit code (local) or the task's last result (remote); $null when not waited for.
.NOTES
    Throws "Nobody is signed in on <name>: sign in there (or restore its Ready checkpoint) first." when no console
    user exists.
#>
function Invoke-InDesktopSession {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Execute,
        [string]$Argument = '',
        [switch]$Highest,
        [int]$WaitSeconds = 0
    )
    $m = Get-TestMachine -Name $Name
    if ($m.Kind -eq 'Local') {
        $sp = @{ FilePath = $Execute; PassThru = $true }
        if ($Argument) { $sp['ArgumentList'] = $Argument }
        $p = Start-Process @sp
        if ($WaitSeconds -gt 0) {
            if ($p.WaitForExit($WaitSeconds * 1000)) { return $p.ExitCode }
            return $null
        }
        return $null
    }

    # Win32_ComputerSystem.UserName names only the CONSOLE user. A VMConnect enhanced session (and any Remote Desktop
    # sign-in) is not the console, so fall back to the owner of a running explorer.exe: that user has a desktop.
    $user = Invoke-TestComputer -Name $Name -ScriptBlock {
        $u = (Get-CimInstance Win32_ComputerSystem).UserName
        if (-not $u) {
            $owner = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" |
                ForEach-Object { Invoke-CimMethod -InputObject $_ -MethodName GetOwner } |
                Where-Object { $_.ReturnValue -eq 0 } | Select-Object -First 1
            if ($owner) { $u = $owner.Domain + '\' + $owner.User }
        }
        $u
    }
    if (-not $user) { throw "Nobody is signed in on $($m.Name): sign in there (or restore its Ready checkpoint) first." }
    $taskName = 'TermPointFieldTest-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $block = {
        param($taskName, $exe, $arg, $user, $highest, $wait)
        $ErrorActionPreference = 'Stop'
        if ($arg) { $action = New-ScheduledTaskAction -Execute $exe -Argument $arg } else { $action = New-ScheduledTaskAction -Execute $exe }
        $level = 'Limited'
        if ($highest) { $level = 'Highest' }
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel $level
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
        $result = $null
        try {
            Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
            Start-ScheduledTask -TaskName $taskName
            Start-Sleep -Seconds 2
            if ($wait -gt 0) {
                $deadline = (Get-Date).AddSeconds($wait)
                while ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
                if ((Get-ScheduledTask -TaskName $taskName).State -ne 'Running') { $result = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult }
            }
            else { Start-Sleep -Seconds 2 }
        }
        finally { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue }
        return $result
    }
    return (Invoke-TestComputer -Name $Name -ScriptBlock $block -ArgumentList @($taskName, $Execute, $Argument, [string]$user, [bool]$Highest, $WaitSeconds))
}

<#
.SYNOPSIS
    Runs net use for the test share (the password prompt comes from net use itself).
.DESCRIPTION
    Runs: net use <ShareRoot> /user:<ShareUser>. Do it in the elevated window you run the other commands in.
.EXAMPLE
    Connect-TestShare
.NOTES
    Throws when net use fails or ShareRoot / ShareUser are not in the config.
#>
function Connect-TestShare {
    [CmdletBinding()]
    param()
    $cfg = Get-TestConfig
    if (-not $cfg.ShareRoot -or -not $cfg.ShareUser) { throw 'ShareRoot and ShareUser must be set in the config.' }
    & net.exe use $cfg.ShareRoot "/user:$($cfg.ShareUser)"
    if ($LASTEXITCODE -ne 0) { throw "net use failed (exit code $LASTEXITCODE)." }
}

#endregion

#region ---------------------------------------------------------------- TermPoint processes

<# Internal: TermPoint processes on one machine (Id, StartTime, Responding, Suspended). #>
function Get-TermPointProcessOn {
    param([Parameter(Mandatory)][string]$Name)
    $block = {
        Get-Process -Name TermPoint -ErrorAction SilentlyContinue | ForEach-Object {
            $susp = $false
            try {
                $th = @($_.Threads)
                $susp = ($th.Count -gt 0) -and (@($th | Where-Object { $_.ThreadState -ne 'Wait' -or $_.WaitReason -ne 'Suspended' }).Count -eq 0)
            } catch { $susp = $false }
            $resp = $true
            try { $resp = [bool]$_.Responding } catch { $resp = $false }
            [pscustomobject]@{ Id = $_.Id; StartTime = $_.StartTime; Responding = $resp; Suspended = $susp }
        }
    }
    foreach ($p in @(Invoke-TestComputer -Name $Name -ScriptBlock $block)) {
        if ($null -eq $p) { continue }
        [pscustomobject]@{ Machine = $Name; Id = [int]$p.Id; StartTime = $p.StartTime; Responding = $p.Responding; Suspended = $p.Suspended }
    }
}

<#
.SYNOPSIS
    Lists TermPoint processes.
.PARAMETER Machine
    Machine name(s); default all checkable machines (Local, Running VMs, enabled WinRM). VMs are skipped with a
    warning when the console is not elevated.
.OUTPUTS
    Machine, Id, StartTime, Responding, Suspended (true when every thread is waiting with reason Suspended).
.EXAMPLE
    Get-TermPointProcess
#>
function Get-TermPointProcess {
    [CmdletBinding()]
    param([string[]]$Machine)
    if ($Machine) { $names = @($Machine | ForEach-Object { (Get-TestMachine -Name $_).Name }) }
    else { $names = @(Get-ActiveTestMachine | ForEach-Object { $_.Name }) }
    foreach ($n in $names) { Get-TermPointProcessOn -Name $n }
}

<#
.SYNOPSIS
    Internal safety check: throws if TermPoint might be running on any machine.
.PARAMETER Action
    Words for the error ("reset the database").
.NOTES
    Strict (see Get-ActiveTestMachine): unverifiable VM situations refuse rather than guess.
#>
function Assert-TermPointNotRunning {
    param([string]$Action = 'continue')
    foreach ($m in @(Get-ActiveTestMachine -Strict)) {
        $procs = @(Get-TermPointProcessOn -Name $m.Name)
        if ($procs.Count -gt 0) {
            throw "TermPoint is running on $($m.Name) (PID $($procs[0].Id)). Close it everywhere before you $Action."
        }
    }
}

<#
.SYNOPSIS
    Internal: turns what the user passed as -Id into a PID.
.PARAMETER Value
    A number, or an object with an Id property (what Start-TermPoint and Get-TermPointProcess return).
.OUTPUTS
    The PID as an int.
.NOTES
    Throws when the value is neither.
#>
function Resolve-TermPointId {
    param([Parameter(Mandatory)][object]$Value)
    if ($Value -is [System.Array]) {
        if ($Value.Count -ne 1) { throw "-Id got $($Value.Count) processes; pass one." }
        $Value = $Value[0]
    }
    if ($null -ne $Value.PSObject.Properties['Id']) { $Value = $Value.Id }
    $n = 0
    if (-not [int]::TryParse([string]$Value, [ref]$n)) { throw "-Id must be a PID or the object Start-TermPoint returns; got '$Value'." }
    return $n
}

<# Internal: refuses (throws) unless the PID on the machine is a TermPoint process. #>
function Assert-IsTermPointProcess {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][int]$Id)
    $found = Invoke-TestComputer -Name $Name -ArgumentList @($Id) -ScriptBlock {
        param($id)
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($p) { $p.ProcessName } else { '' }
    }
    if ($found -ne 'TermPoint') {
        if ($found) { $what = "process '$found'" } else { $what = 'no process' }
        throw "PID $Id on $Name is $what, not TermPoint. Refusing."
    }
}

<#
.SYNOPSIS
    Closes TermPoint on a machine, politely or as a crash.
.PARAMETER Machine
    Machine name.
.PARAMETER Id
    PID, or the object Start-TermPoint / Get-TermPointProcess returns; must be a process named TermPoint.
.PARAMETER Hard
    Stop-Process -Force: a true crash; the lock file should stay on the share.
.DESCRIPTION
    Without -Hard sends a close request (taskkill /PID, no /F) in the signed-in user's desktop session, so WM_CLOSE
    reaches the window. The app may still show a prompt.
.EXAMPLE
    Stop-TermPoint -Machine TPVM1 -Id 1234 -Hard
.NOTES
    Throws when the PID is not TermPoint.
#>
function Stop-TermPoint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [Parameter(Mandatory)][object]$Id, [switch]$Hard)
    $Id = Resolve-TermPointId -Value $Id
    $m = Get-TestMachine -Name $Machine
    Assert-IsTermPointProcess -Name $m.Name -Id $Id
    if ($Hard) {
        Invoke-TestComputer -Name $m.Name -ArgumentList @($Id) -ScriptBlock { param($id) Stop-Process -Id $id -Force }
        Write-Step "Hard-killed TermPoint PID $Id on $($m.Name); the lock file should stay on the share."
    }
    else {
        Invoke-InDesktopSession -Name $m.Name -Execute 'taskkill.exe' -Argument "/PID $Id" -WaitSeconds 20 | Out-Null
        Write-Step "Sent a close request to TermPoint PID $Id on $($m.Name)."
    }
}

<# Internal: suspends or resumes a TermPoint PID with NtSuspendProcess / NtResumeProcess in the target session. #>
function Set-TermPointSuspension {
    param([string]$Machine, [int]$Id, [bool]$Resume)
    $m = Get-TestMachine -Name $Machine
    Assert-IsTermPointProcess -Name $m.Name -Id $Id
    Invoke-TestComputer -Name $m.Name -ArgumentList @($Id, $script:NtSource, $Resume) -ScriptBlock {
        param($id, $src, $resume)
        if (-not ('FieldTestNt' -as [type])) { Add-Type -TypeDefinition $src }
        $status = [FieldTestNt]::Apply($id, $resume)
        if ($status -ne 0) { throw ("NtSuspendProcess/NtResumeProcess returned 0x{0:X8}" -f $status) }
    }
}

<#
.SYNOPSIS
    Freezes a TermPoint process (NtSuspendProcess), like a laptop going to sleep without the clock stopping.
.PARAMETER Machine
    Machine name.
.PARAMETER Id
    PID of a TermPoint process, or the object Start-TermPoint returns.
.NOTES
    Throws when the PID is not TermPoint or the call fails.
#>
function Suspend-TermPoint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [Parameter(Mandatory)][object]$Id)
    $Id = Resolve-TermPointId -Value $Id
    Set-TermPointSuspension -Machine $Machine -Id $Id -Resume $false
    Write-Step "Suspended TermPoint PID $Id on $Machine."
}

<#
.SYNOPSIS
    Thaws a process frozen by Suspend-TermPoint (NtResumeProcess).
.PARAMETER Machine
    Machine name.
.PARAMETER Id
    PID of the TermPoint process, or the object Start-TermPoint returns.
#>
function Resume-TermPoint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [Parameter(Mandatory)][object]$Id)
    $Id = Resolve-TermPointId -Value $Id
    Set-TermPointSuspension -Machine $Machine -Id $Id -Resume $true
    Write-Step "Resumed TermPoint PID $Id on $Machine."
}

<# Internal: the installed TermPoint package on a machine (Name, Version, PackageFamilyName) or $null. #>
function Get-TermPointPackage {
    param([Parameter(Mandatory)][string]$Name)
    $cfg = Get-TestConfig
    $p = Invoke-TestComputer -Name $Name -ArgumentList @($cfg.MsixPackageName) -ScriptBlock {
        param($pkg)
        $r = $null
        if ($PSVersionTable.PSEdition -eq 'Core') {
            # PowerShell 7 cannot load the Appx module reliably: ask Windows PowerShell and read a simple text line back.
            $cmd = "try { `$r = @(Get-AppxPackage -AllUsers -Name '$pkg' -ErrorAction Stop) } catch { `$r = @(Get-AppxPackage -Name '$pkg') }; " +
                   "`$f = `$r | Select-Object -First 1; if (`$f) { `$f.Name + '|' + `$f.Version + '|' + `$f.PackageFamilyName }"
            $line = & powershell.exe -NoProfile -NonInteractive -Command $cmd
            if ($line) { $parts = ([string]$line).Split('|'); return [pscustomobject]@{ Name = $parts[0]; Version = $parts[1]; PackageFamilyName = $parts[2] } }
            return
        }
        try { $r = @(Get-AppxPackage -AllUsers -Name $pkg -ErrorAction Stop) } catch { $r = @(Get-AppxPackage -Name $pkg -ErrorAction SilentlyContinue) }
        $first = $r | Select-Object -First 1
        if ($first) { [pscustomobject]@{ Name = $first.Name; Version = [string]$first.Version; PackageFamilyName = $first.PackageFamilyName } }
    }
    return $p
}

<#
.SYNOPSIS
    Installs the sideload MSIX on a machine for its signed-in user.
.DESCRIPTION
    Copies the MSIX and certificate to "$env:TEMP\TermPointFieldTest\" on the target, imports the certificate into
    Cert:\LocalMachine\Root unless its thumbprint is already there (needs elevation), runs Add-AppxPackage in the
    signed-in user's desktop session, waits, and prints the installed version.
.PARAMETER Machine
    Machine name.
.PARAMETER MsixPath
    The package on this computer.
.PARAMETER CertPath
    Its .cer on this computer.
.NOTES
    Throws when a file is missing, nobody is signed in on the target, or the certificate cannot be imported.
    The signed-in user must be able to read the target's temp folder (true when it is the same account).
#>
function Install-TermPoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Machine,
        [string]$MsixPath = 'C:\temp\termpoint-msix\TermPoint.msix',
        [string]$CertPath = 'C:\temp\termpoint-msix\TermPoint-cert.cer'
    )
    $m = Get-TestMachine -Name $Machine
    foreach ($f in $MsixPath, $CertPath) { if (-not (Test-Path -LiteralPath $f)) { throw "File not found: $f" } }
    $dir = Invoke-TestComputer -Name $m.Name -ScriptBlock {
        $d = Join-Path $env:TEMP 'TermPointFieldTest'
        New-Item -ItemType Directory -Force -Path $d | Out-Null
        $d
    }
    $targetMsix = $dir + '\' + (Split-Path -Leaf $MsixPath)
    $targetCert = $dir + '\' + (Split-Path -Leaf $CertPath)
    $s = Connect-TestComputer -Name $m.Name
    foreach ($pair in @(@($MsixPath, $targetMsix), @($CertPath, $targetCert))) {
        Write-Step "Copying $(Split-Path -Leaf $pair[0]) to $($m.Name)..."
        if ($s) { Copy-Item -ToSession $s -LiteralPath $pair[0] -Destination $pair[1] -Force }
        elseif ($pair[0] -ne $pair[1]) { Copy-Item -LiteralPath $pair[0] -Destination $pair[1] -Force }
    }
    $certResult = Invoke-TestComputer -Name $m.Name -ArgumentList @($targetCert) -ScriptBlock {
        param($certFile)
        $thumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $certFile).Thumbprint
        if (Test-Path -LiteralPath "Cert:\LocalMachine\Root\$thumb") { return "present ($thumb)" }
        Import-Certificate -FilePath $certFile -CertStoreLocation 'Cert:\LocalMachine\Root' -ErrorAction Stop | Out-Null
        return "imported ($thumb)"
    }
    Write-Step "Certificate: $certResult"
    $script = "try { Add-AppxPackage -Path '$($targetMsix.Replace("'", "''"))' -ErrorAction Stop } catch { exit 1 }"
    $arg = '-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ' + (ConvertTo-EncodedCommand -Script $script)
    Write-Step "Installing on $($m.Name) (in the signed-in user's session)..."
    $code = Invoke-InDesktopSession -Name $m.Name -Execute 'powershell.exe' -Argument $arg -WaitSeconds 240
    if ($null -eq $code) { Write-Warning 'The install did not finish within 4 minutes; check the machine.' }
    elseif ($code -ne 0) { Write-Warning "Add-AppxPackage reported failure (code $code). Is the same version already installed (uninstall it first), or is the certificate untrusted?" }
    $pkg = Get-TermPointPackage -Name $m.Name
    if ($pkg) { Write-Step "Installed on $($m.Name): $($pkg.Name) version $($pkg.Version)" } else { Write-Warning "No $((Get-TestConfig).MsixPackageName) package found on $($m.Name) afterwards." }
}

<#
.SYNOPSIS
    Launches TermPoint in the signed-in user's desktop session on a machine.
.DESCRIPTION
    MSIX: explorer.exe "shell:AppsFolder\<PackageFamilyName>!TermPoint"; with ExePath in the config: that exe.
    Waits up to 30 s for a TermPoint process that started after the launch. If one is already running it says so and
    returns it without launching.
.PARAMETER Machine
    Machine name.
.OUTPUTS
    Machine, Id, StartTime.
.NOTES
    Throws when nobody is signed in, the package is not installed, or no process appears within 30 s.
#>
function Start-TermPoint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine)
    $m = Get-TestMachine -Name $Machine
    $cfg = Get-TestConfig
    $existing = @(Get-TermPointProcessOn -Name $m.Name)
    if ($existing.Count -gt 0) {
        Write-Step "TermPoint is already running on $($m.Name) (PID $($existing[0].Id)); not launching another."
        return [pscustomobject]@{ Machine = $m.Name; Id = $existing[0].Id; StartTime = $existing[0].StartTime }
    }
    $before = Invoke-TestComputer -Name $m.Name -ScriptBlock { Get-Date }
    # Both routes go through explorer.exe, so the shell (not the one-shot scheduled task, which is deleted seconds
    # later and has a run-time limit) owns the TermPoint process.
    if ($cfg.ExePath) { $exe = 'explorer.exe'; $arg = '"' + $cfg.ExePath + '"' }
    else {
        $pkg = Get-TermPointPackage -Name $m.Name
        if (-not $pkg) { throw "$($cfg.MsixPackageName) is not installed on $($m.Name). Run Install-TermPoint first." }
        $exe = 'explorer.exe'
        $arg = 'shell:AppsFolder\' + $pkg.PackageFamilyName + '!TermPoint'
    }
    Write-Step "Launching TermPoint on $($m.Name)..."
    Invoke-InDesktopSession -Name $m.Name -Execute $exe -Argument $arg -WaitSeconds 10 | Out-Null
    $found = Invoke-TestComputer -Name $m.Name -ArgumentList @($before) -ScriptBlock {
        param($since)
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            $p = Get-Process -Name TermPoint -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $since } | Select-Object -First 1
            if ($p) { return [pscustomobject]@{ Id = $p.Id; StartTime = $p.StartTime } }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $found) { throw "TermPoint did not start on $($m.Name) within 30 s." }
    Write-Step "TermPoint started on $($m.Name): PID $($found.Id)."
    return [pscustomobject]@{ Machine = $m.Name; Id = [int]$found.Id; StartTime = $found.StartTime }
}

#endregion

#region ---------------------------------------------------------------- database commands

<#
.SYNOPSIS
    Puts the shared test database back to the pristine Master.
.DESCRIPTION
    Refuses while TermPoint runs on any machine (Local always; VMs when Running, and also when Paused/Saved since
    TermPoint could be alive inside; WinRM when enabled; the console must be elevated when the config has a VM).
    Refuses while the lock file exists unless -Force. Then deletes D's lock, .lock.tmp, D.tmp, D-wal and D-shm,
    copies Master over D, compares SHA-256 of D and Master (and MasterHash if configured), prints one line per step
    and records the time in "<WorkFolder>\last-reset.txt" (the default -Since of the log commands).
    D itself need not exist (first reset); the share folder must be visible.
.PARAMETER Force
    Reset even though a lock file exists.
.PARAMETER PassThru
    Also return a result object (D hash, Master hash, time).
.EXAMPLE
    Reset-TestDatabase -Force
.NOTES
    Throws when the share cannot be seen, TermPoint may be running, the lock exists (no -Force), or the copy's hash
    does not match Master.
#>
function Reset-TestDatabase {
    [CmdletBinding()]
    param([switch]$Force, [switch]$PassThru)
    $cfg = Get-TestConfig
    $p = Get-TestDatabasePath -Config $cfg
    if (-not (Test-Path -LiteralPath $p.Dir)) { throw "Can't see the test database from this window. In an elevated window run Connect-TestShare first." }
    if (-not (Test-Path -LiteralPath $cfg.Master)) { throw "Master copy not found: $($cfg.Master)" }

    Assert-TermPointNotRunning -Action 'reset the database'
    Write-Step 'TermPoint is not running on any checked computer.'
    if ((Test-Path -LiteralPath $p.Lock) -and -not $Force) {
        throw "The lock file exists ($($p.Lock)). Close TermPoint everywhere, or use -Force to delete it."
    }
    foreach ($f in $p.Lock, $p.LockTmp, $p.DbTmp, $p.Wal, $p.Shm) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force; Write-Step "Deleted $(Split-Path -Leaf $f)" }
    }
    $masterHash = Get-Sha256 -Path $cfg.Master
    if ($cfg.MasterHash -and $masterHash -ne $cfg.MasterHash.ToUpperInvariant()) {
        Write-Warning "Master no longer matches MasterHash in the config (master $(Get-ShortHash $masterHash), config $(Get-ShortHash $cfg.MasterHash))."
    }
    Copy-Item -LiteralPath $cfg.Master -Destination $p.Db -Force
    Write-Step "Copied Master over $($p.Name)"
    $dHash = Get-Sha256 -Path $p.Db
    if ($dHash -ne $masterHash) { throw "After the copy, $($p.Name) ($(Get-ShortHash $dHash)) does not match Master ($(Get-ShortHash $masterHash))." }
    Write-Step "Hashes match: D = Master = $(Get-ShortHash $dHash)"
    $when = Get-Date
    Initialize-Folder -Path $cfg.WorkFolder | Out-Null
    Set-Content -LiteralPath (Join-Path $cfg.WorkFolder 'last-reset.txt') -Value $when.ToString('yyyy-MM-ddTHH:mm:ss') -Encoding ASCII
    Write-Step 'Recorded the reset time.'
    if ($PassThru) { [pscustomobject]@{ ResetTime = $when; DatabaseHash = $dHash; MasterHash = $masterHash } }
}

<# Internal: reads a text file without blocking the writer (FileShare ReadWrite + Delete). #>
function Read-TextShared {
    param([Parameter(Mandatory)][string]$Path)
    $fs = New-Object System.IO.FileStream($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { $sr = New-Object System.IO.StreamReader($fs, [Text.Encoding]::UTF8); try { return $sr.ReadToEnd() } finally { $sr.Dispose() } }
    finally { $fs.Dispose() }
}

<# Internal: converts a JSON date value (string, or DateTime from PowerShell 7) to UTC; $null if unparseable. #>
function ConvertTo-UtcTime {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    $o = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse([string]$Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$o)) { return $o.UtcDateTime }
    return $null
}

<#
.SYNOPSIS
    Internal: reads and parses D's lock file; never throws.
.OUTPUTS
    [pscustomobject] Exists, Error, and the lock's fields (Username, Machine, SessionGuid, Acquired, Heartbeat (local
    time), HeartbeatAgeSeconds, ProcessId, ProcessStartTimeUtc, TimingScale).
#>
function Read-TestLockFile {
    param([Parameter(Mandatory)][string]$LockPath)
    $r = [ordered]@{ Exists = $false; Error = $null; Username = $null; Machine = $null; SessionGuid = $null; Acquired = $null
                     Heartbeat = $null; HeartbeatAgeSeconds = $null; ProcessId = $null; ProcessStartTimeUtc = $null; TimingScale = $null }
    try {
        if (-not (Test-Path -LiteralPath $LockPath)) { return [pscustomobject]$r }
        $r.Exists = $true
        $j = Read-TextShared -Path $LockPath | ConvertFrom-Json
        foreach ($k in 'Username', 'Machine', 'SessionGuid', 'ProcessId', 'TimingScale') {
            if ($j.PSObject.Properties[$k]) { $r[$k] = $j.$k }
        }
        $acq = $null; $hb = $null
        if ($j.PSObject.Properties['Acquired']) { $acq = ConvertTo-UtcTime $j.Acquired }
        if ($j.PSObject.Properties['Heartbeat']) { $hb = ConvertTo-UtcTime $j.Heartbeat }
        if ($j.PSObject.Properties['ProcessStartTimeUtc']) { $r.ProcessStartTimeUtc = $j.ProcessStartTimeUtc }
        if ($acq) { $r.Acquired = $acq.ToLocalTime() }
        if ($hb) { $r.Heartbeat = $hb.ToLocalTime(); $r.HeartbeatAgeSeconds = [Math]::Round(([datetime]::UtcNow - $hb).TotalSeconds, 0) }
    }
    catch { $r.Error = $_.Exception.Message }
    return [pscustomobject]$r
}

<#
.SYNOPSIS
    Shows the state of the shared test database: hash, whether it matches Master, leftovers and the lock file.
.DESCRIPTION
    Read-only; works from a non-elevated console. Lock read errors are reported, not thrown (the lock file is
    rewritten every 60 s, so a read can fail).
.PARAMETER PassThru
    Also return the state as an object.
.OUTPUTS
    With -PassThru: DatabaseHash, MatchesMaster, LastWriteTime, Leftovers, Lock.
.EXAMPLE
    Get-TestDatabaseState
.NOTES
    Throws only when D cannot be seen.
#>
function Get-TestDatabaseState {
    [CmdletBinding()]
    param([switch]$PassThru)
    $cfg = Get-TestConfig
    Assert-DatabaseVisible -Config $cfg
    $p = Get-TestDatabasePath -Config $cfg
    $hash = Get-Sha256 -Path $p.Db
    $same = $null
    if (Test-Path -LiteralPath $cfg.Master) { $same = ($hash -eq (Get-Sha256 -Path $cfg.Master)) }
    $left = @()
    foreach ($pair in @(@('.lock.tmp', $p.LockTmp), @('.db.tmp', $p.DbTmp), @('-wal', $p.Wal), @('-shm', $p.Shm))) {
        if (Test-Path -LiteralPath $pair[1]) { $left += $pair[0] }
    }
    $written = (Get-Item -LiteralPath $p.Db).LastWriteTime
    $lock = Read-TestLockFile -LockPath $p.Lock

    Write-Host "Database   : $($p.Db)"
    Write-Host "  hash        $(Get-ShortHash $hash)   matches master: $same"
    Write-Host "  written     $($written.ToString('yyyy-MM-dd HH:mm:ss'))"
    if ($left.Count -gt 0) { Write-Host "  leftovers   $($left -join ', ')" } else { Write-Host '  leftovers   none' }
    if (-not $lock.Exists) { Write-Host '  lock        none' }
    elseif ($lock.Error) { Write-Host "  lock        exists, could not be read: $($lock.Error)" }
    else {
        Write-Host "  lock        $($lock.Username) on $($lock.Machine), session $(Get-ShortHash $lock.SessionGuid), PID $($lock.ProcessId)"
        if ($lock.Acquired) { Write-Host "              acquired $($lock.Acquired.ToString('HH:mm:ss'))" }
        if ($lock.Heartbeat) { Write-Host "              heartbeat $($lock.Heartbeat.ToString('HH:mm:ss')) ($($lock.HeartbeatAgeSeconds) s ago)" }
        if ($null -ne $lock.TimingScale) { Write-Host "              timing scale $($lock.TimingScale)" }
    }
    if ($PassThru) {
        [pscustomobject]@{ DatabaseHash = $hash; MatchesMaster = $same; LastWriteTime = $written; Leftovers = $left; Lock = $lock }
    }
}

<#
.SYNOPSIS
    Samples the lock file every few seconds and appends one line per sample to a file.
.DESCRIPTION
    Each line: time, lock holder machine, session, heartbeat time, D's last-write time, and "tmp" while D.tmp exists.
    Read-only; works from a non-elevated console. Press Ctrl+C to stop early.
.PARAMETER Minutes
    How long to watch (default 60).
.PARAMETER IntervalSeconds
    Time between samples (default 20).
.PARAMETER Out
    Output file (default "<WorkFolder>\lockwatch-yyyyMMdd-HHmmss.txt").
.EXAMPLE
    Watch-TestLock -Minutes 30
.NOTES
    Throws when D cannot be seen at the start. Read errors during the run are written as lines.
#>
function Watch-TestLock {
    [CmdletBinding()]
    param([int]$Minutes = 60, [int]$IntervalSeconds = 20, [string]$Out)
    $cfg = Get-TestConfig
    Assert-DatabaseVisible -Config $cfg
    $p = Get-TestDatabasePath -Config $cfg
    if (-not $Out) {
        Initialize-Folder -Path $cfg.WorkFolder | Out-Null
        $Out = Join-Path $cfg.WorkFolder ('lockwatch-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
    }
    Write-Host "Watching $($p.Lock) for $Minutes min; writing to $Out"
    $end = (Get-Date).AddMinutes($Minutes)
    while ((Get-Date) -lt $end) {
        $now = Get-Date -Format 'HH:mm:ss'
        $lock = Read-TestLockFile -LockPath $p.Lock
        if (-not $lock.Exists) { $line = "$now  no lock" }
        elseif ($lock.Error) { $line = "$now  read error: $($lock.Error)" }
        else {
            $hbText = '?'
            if ($lock.Heartbeat) { $hbText = $lock.Heartbeat.ToString('HH:mm:ss') }
            $line = "$now  $($lock.Machine)  sess=$(Get-ShortHash $lock.SessionGuid)  hb=$hbText"
        }
        $dw = '?'
        try { $dw = (Get-Item -LiteralPath $p.Db -ErrorAction Stop).LastWriteTime.ToString('HH:mm:ss') } catch { $dw = 'unreadable' }
        $tmp = ''
        if (Test-Path -LiteralPath $p.DbTmp) { $tmp = ' tmp' }
        $line = "$line  D=$dw$tmp"
        Add-Content -LiteralPath $Out -Value $line -Encoding ASCII
        Write-Host $line
        Start-Sleep -Seconds $IntervalSeconds
    }
}

<# Internal: runs a python helper with the configured interpreter; returns its output lines; throws on non-zero exit. #>
function Invoke-TestPython {
    param([Parameter(Mandatory)][string]$Script, [string[]]$Arguments = @(), [switch]$AllowFailure)
    $cfg = Get-TestConfig
    $scriptPath = Join-Path (Join-Path $PSScriptRoot 'python') $Script
    $all = @($cfg.PythonArgs) + @($scriptPath) + @($Arguments)
    $out = @(& $cfg.PythonExe @all 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) { throw "$Script failed (exit $LASTEXITCODE): $($out -join ' | ')" }
    return $out
}

<#
.SYNOPSIS
    Plays "Alice on another computer" by writing or deleting D's lock file.
.DESCRIPTION
    Runs python\fake_lock.py on D (it only writes/deletes the lock file; SQLite never opens D).
      Takeover - Alice holds the lock now (run while TermPoint is writer: its next save is refused).
      Stale    - Alice's lock, 10 minutes old (run with TermPoint closed, then open the database).
      Clear    - delete the lock file; refuses while TermPoint runs anywhere (same check as Reset-TestDatabase).
.PARAMETER Kind
    Takeover, Stale or Clear.
.EXAMPLE
    Set-TestLock -Kind Stale
.NOTES
    Throws when D cannot be seen, or (Clear) TermPoint may be running or VMs cannot be checked.
#>
function Set-TestLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Takeover', 'Stale', 'Clear')][string]$Kind)
    $cfg = Get-TestConfig
    Assert-DatabaseVisible -Config $cfg
    if ($Kind -eq 'Clear') { Assert-TermPointNotRunning -Action 'clear the lock' }
    Invoke-TestPython -Script 'fake_lock.py' -Arguments @($Kind.ToLowerInvariant(), $cfg.Database) | ForEach-Object { Write-Host $_ }
}

<#
.SYNOPSIS
    Simulates a colleague's save without SQLite over SMB.
.DESCRIPTION
    Copies D to "<WorkFolder>\colleague\", runs python\colleague.py on that LOCAL copy, copies the edited file beside D
    as "<D>.colleague.tmp" (checking its hash), then renames it onto D in one step (Move-Item -Force). Prints the new short hash.
.EXAMPLE
    Edit-TestDatabaseAsColleague
.NOTES
    Throws when D cannot be seen, D-wal exists (D should not have one), or a copy's hash does not match.
#>
function Edit-TestDatabaseAsColleague {
    [CmdletBinding()]
    param()
    $cfg = Get-TestConfig
    Assert-DatabaseVisible -Config $cfg
    $p = Get-TestDatabasePath -Config $cfg
    if (Test-Path -LiteralPath $p.Wal) { throw "$($p.Name)-wal exists beside D; a colleague's rename would orphan it. Investigate first." }
    $dir = Initialize-Folder -Path (Join-Path $cfg.WorkFolder 'colleague')
    $local = Join-Path $dir $p.Name
    if (Test-Path -LiteralPath $local) { Remove-Item -LiteralPath $local -Force }
    Copy-Item -LiteralPath $p.Db -Destination $local -Force
    $before = Get-Sha256 -Path $p.Db
    if ((Get-Sha256 -Path $local) -ne $before) { throw 'The local copy of D does not match D (D changed while copying); try again.' }
    Invoke-TestPython -Script 'colleague.py' -Arguments @('colleague', $local) | ForEach-Object { Write-Host $_ }
    $edited = Get-Sha256 -Path $local
    $tmp = $p.Db + '.colleague.tmp'
    Copy-Item -LiteralPath $local -Destination $tmp -Force
    if ((Get-Sha256 -Path $tmp) -ne $edited) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue; throw 'The copy beside D is torn; nothing was changed.' }
    # One rename onto D, as a real save does (see $script:MoveSource for why not Move-Item -Force).
    if (-not ('FieldTestFile' -as [type])) { Add-Type -TypeDefinition $script:MoveSource }
    [FieldTestFile]::ReplaceByRename($tmp, $p.Db)
    Write-Step "D replaced by the colleague's save: $(Get-ShortHash $before) -> $(Get-ShortHash (Get-Sha256 -Path $p.Db))"
}

<#
.SYNOPSIS
    Collects D and every machine's kept working copies and lists the field-test sections in each.
.DESCRIPTION
    Copies D (plus D-wal if present) to "<WorkFolder>\sections-yyyyMMdd-HHmmss\shared\" and, per machine, the working
    copies in %APPDATA%\TermPoint\working and each MSIX package's LocalCache\Roaming\TermPoint\working to
    "...\<machine>\appdata\" or "...\<machine>\msix-<package>\". The writer copy for D is "<P>_<D name>" and the
    read-only snapshot "<P>_<D name without ext>_ro<ext>", P = first 8 hex chars, UPPERCASE, of the SHA-256 of the UTF-8
    bytes of the lower-cased full path of D; other "*_<D name>" / "*_<name>_ro<ext>" files are labelled "same name,
    other path". Each .db is copied with its -wal and .dirty marker (marker JSON is summarised). .savetmp and .tmp are
    ignored. Then python\sections.py runs over all copies. Working copies on machines are optional: none is fine.
.PARAMETER Machine
    Machines to look at (default all checkable ones).
.PARAMETER Pattern
    Text to look for in section codes (default ZZZT).
.NOTES
    Throws when D cannot be seen. Warns when TermPoint runs on a machine (its copies may be torn).
#>
function Get-TestSections {
    [CmdletBinding()]
    param([string[]]$Machine, [string]$Pattern = 'ZZZT')
    $cfg = Get-TestConfig
    Assert-DatabaseVisible -Config $cfg
    $p = Get-TestDatabasePath -Config $cfg
    $root = Initialize-Folder -Path (Join-Path $cfg.WorkFolder ('sections-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
    $copies = New-Object System.Collections.Generic.List[string]

    # D itself (and its -wal if one is there), copied as plain files.
    $shared = Initialize-Folder -Path (Join-Path $root 'shared')
    Copy-Item -LiteralPath $p.Db -Destination (Join-Path $shared $p.Name) -Force
    $copies.Add((Join-Path $shared $p.Name))
    if (Test-Path -LiteralPath $p.Wal) { Copy-Item -LiteralPath $p.Wal -Destination (Join-Path $shared ($p.Name + '-wal')) -Force; Write-Host '  (D has a -wal; copied with it)' }

    # P: first 8 hex chars, uppercase, of SHA-256 over the UTF-8 bytes of the lower-cased full path of D.
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($p.Db).ToLowerInvariant()))
    $prefix = ([BitConverter]::ToString($bytes) -replace '-', '').Substring(0, 8).ToUpperInvariant()
    $writerName = $prefix + '_' + $p.Name
    $roSuffix = $p.NameNoExt + '_ro' + $p.Ext
    $roName = $prefix + '_' + $roSuffix

    if ($Machine) { $names = @($Machine | ForEach-Object { (Get-TestMachine -Name $_).Name }) }
    else { $names = @(Get-ActiveTestMachine | ForEach-Object { $_.Name }) }

    $finder = {
        param($dName, $writerName, $roName, $roSuffix)
        $dirs = @()
        $dirs += [pscustomobject]@{ Tag = 'appdata'; Path = (Join-Path $env:APPDATA 'TermPoint\working') }
        $pk = Join-Path $env:LOCALAPPDATA 'Packages'
        if (Test-Path -LiteralPath $pk) {
            foreach ($d in @(Get-ChildItem -LiteralPath $pk -Directory -Filter '*TermPoint*' -ErrorAction SilentlyContinue)) {
                $dirs += [pscustomobject]@{ Tag = 'msix-' + $d.Name; Path = (Join-Path $d.FullName 'LocalCache\Roaming\TermPoint\working') }
            }
        }
        foreach ($d in $dirs) {
            if (-not (Test-Path -LiteralPath $d.Path)) { continue }
            foreach ($f in @(Get-ChildItem -LiteralPath $d.Path -File -ErrorAction SilentlyContinue)) {
                $n = $f.Name
                $label = $null
                if ($n -ieq $writerName) { $label = 'writer copy' }
                elseif ($n -ieq $roName) { $label = 'read-only snapshot' }
                elseif ($n.EndsWith('_' + $dName, [StringComparison]::OrdinalIgnoreCase)) { $label = 'same name, other path' }
                elseif ($n.EndsWith('_' + $roSuffix, [StringComparison]::OrdinalIgnoreCase)) { $label = 'same name, other path (read-only snapshot)' }
                if (-not $label) { continue }
                $wal = ''; $dirty = ''
                if (Test-Path -LiteralPath ($f.FullName + '-wal')) { $wal = $f.FullName + '-wal' }
                if (Test-Path -LiteralPath ($f.FullName + '.dirty')) { $dirty = $f.FullName + '.dirty' }
                [pscustomobject]@{ Tag = $d.Tag; Name = $n; Path = $f.FullName; Label = $label; Wal = $wal; Dirty = $dirty }
            }
        }
    }

    foreach ($n in $names) {
        Write-Host "== $n"
        $running = @(Get-TermPointProcessOn -Name $n)
        if ($running.Count -gt 0) { Write-Warning "TermPoint is running on $n (PID $($running[0].Id)); its copies may be torn." }
        $found = @(Invoke-TestComputer -Name $n -ScriptBlock $finder -ArgumentList @($p.Name, $writerName, $roName, $roSuffix))
        if ($found.Count -eq 0) { Write-Host '  no working copies kept'; continue }
        foreach ($f in $found) {
            $destDir = Initialize-Folder -Path (Join-Path (Join-Path $root $n) $f.Tag)
            $dest = Join-Path $destDir $f.Name
            Copy-FromTestComputer -Name $n -Path $f.Path -Destination $dest
            # The -wal travels with its .db: never one without the other.
            if ($f.Wal) { Copy-FromTestComputer -Name $n -Path $f.Wal -Destination ($dest + '-wal') }
            $copies.Add($dest)
            Write-Host "  $($f.Name)  [$($f.Label)]  wal: $([bool]$f.Wal)"
            if ($f.Dirty) {
                Copy-FromTestComputer -Name $n -Path $f.Dirty -Destination ($dest + '.dirty')
                try {
                    $j = Get-Content -LiteralPath ($dest + '.dirty') -Raw -Encoding UTF8 | ConvertFrom-Json
                    $hc = ''; $ps = ''
                    if ($j.PSObject.Properties['HashAtCheckout']) { $hc = Get-ShortHash $j.HashAtCheckout }
                    if ($j.PSObject.Properties['PendingSaveHash']) { $ps = Get-ShortHash $j.PendingSaveHash }
                    Write-Host "    dirty marker: HashAtCheckout=$hc  PendingSaveHash=$ps"
                } catch { Write-Host "    dirty marker present but unreadable: $($_.Exception.Message)" }
            }
        }
    }

    Write-Host ''
    Write-Host "Copies are in $root"
    Invoke-TestPython -Script 'sections.py' -Arguments (@('--pattern', $Pattern) + @($copies.ToArray())) -AllowFailure | ForEach-Object { Write-Host $_ }
}

#endregion

#region ---------------------------------------------------------------- VM commands

<# Internal: guest UTC clock minus host UTC clock, in seconds, or $null when the guest does not answer. #>
function Measure-GuestClockOffset {
    param([Parameter(Mandatory)][hashtable]$Machine)
    $cred = Get-TestCredential -Name $Machine.Name
    try {
        $t0 = [datetime]::UtcNow
        $guest = Invoke-Command -VMName $Machine.VMName -Credential $cred -ScriptBlock { [datetime]::UtcNow } -ErrorAction Stop
        $t1 = [datetime]::UtcNow
        $mid = $t0.AddTicks([long](($t1 - $t0).Ticks / 2))
        return [Math]::Round((([datetime]$guest).ToUniversalTime() - $mid).TotalSeconds, 1)
    } catch { return $null }
}

<# Internal: waits until PowerShell Direct answers, then measures the guest clock offset; returns it or $null. #>
function Wait-GuestAnswer {
    param([Parameter(Mandatory)][hashtable]$Machine, [int]$TimeoutSeconds = 120)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $o = Measure-GuestClockOffset -Machine $Machine
        if ($null -ne $o) { return $o }
        Start-Sleep -Seconds 3
    }
    return $null
}

<#
.SYNOPSIS
    Freezes a VM for a while (Pause or Save) and reports how its clock behaves afterwards.
.DESCRIPTION
    Pause: Suspend-VM then Resume-VM. Save: Save-VM then Start-VM. Prints start/end times with a countdown line every
    30 s. After resuming, polls up to 120 s until PowerShell Direct answers and reports the guest clock offset
    (guest UTC minus host UTC) at the first answer and 30 s later, to learn whether Hyper-V time sync corrects the
    frozen clock and how fast. Appends a line to "<WorkFolder>\vm-freezes.txt". The VM's cached session is dropped.
.PARAMETER Machine
    A VM machine name.
.PARAMETER Seconds
    How long to keep it frozen.
.PARAMETER Mode
    Pause (default) or Save.
.EXAMPLE
    Suspend-TestComputer -Machine TPVM1 -Seconds 240
.NOTES
    Throws when not elevated, the machine is not a VM, the VM does not exist or is not Running.
#>
function Suspend-TestComputer {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [Parameter(Mandatory)][int]$Seconds, [ValidateSet('Pause', 'Save')][string]$Mode = 'Pause')
    $m = Get-CreatedVmMachine -Name $Machine
    $cfg = Get-TestConfig
    if ((Get-TestVmState -Machine $m) -ne 'Running') { throw "VM '$($m.VMName)' is not Running." }
    Remove-CachedSession -Name $m.Name
    $start = Get-Date
    if ($Mode -eq 'Pause') { Suspend-VM -Name $m.VMName -ErrorAction Stop } else { Save-VM -Name $m.VMName -ErrorAction Stop }
    Write-Step "$($m.Name) frozen ($Mode) for $Seconds s."
    $end = $start.AddSeconds($Seconds)
    while ((Get-Date) -lt $end) {
        $left = [int][Math]::Ceiling(($end - (Get-Date)).TotalSeconds)
        Write-Step "  $left s left"
        Start-Sleep -Seconds ([Math]::Min(30, [Math]::Max(1, $left)))
    }
    if ($Mode -eq 'Pause') { Resume-VM -Name $m.VMName -ErrorAction Stop } else { Start-VM -Name $m.VMName -ErrorAction Stop }
    $resumed = Get-Date
    Write-Step "$($m.Name) resumed."
    $first = Wait-GuestAnswer -Machine $m -TimeoutSeconds 120
    $second = $null
    if ($null -eq $first) { Write-Warning "$($m.Name) did not answer PowerShell Direct within 120 s." }
    else {
        Write-Step "Guest clock offset at first answer: $first s"
        Start-Sleep -Seconds 30
        $second = Measure-GuestClockOffset -Machine $m
        Write-Step "Guest clock offset 30 s later: $second s"
    }
    Initialize-Folder -Path $cfg.WorkFolder | Out-Null
    $line = "{0}  {1}  {2}  frozen {3}s  start {4}  end {5}  offset1={6}  offset2={7}" -f (Get-Date -Format 'yyyy-MM-dd'), $m.Name, $Mode, $Seconds, $start.ToString('HH:mm:ss'), $resumed.ToString('HH:mm:ss'), $first, $second
    Add-Content -LiteralPath (Join-Path $cfg.WorkFolder 'vm-freezes.txt') -Value $line -Encoding ASCII
}

<#
.SYNOPSIS
    Takes a checkpoint of a VM.
.PARAMETER Machine
    A VM machine name.
.PARAMETER Name
    The checkpoint name (e.g. Ready).
.PARAMETER Replace
    Delete an existing checkpoint of the same name first (after the new one is taken, so the VM always has one).
    Without it, an existing name is refused: two checkpoints with one name make Restore-TestComputer ambiguous.
.EXAMPLE
    Checkpoint-TestComputer -Machine TPVM1 -Name Ready -Replace
.NOTES
    Throws when not elevated, the VM does not exist, or the name exists and -Replace is not given.
#>
function Checkpoint-TestComputer {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [Parameter(Mandatory)][string]$Name, [switch]$Replace)
    # Any failure must stop here: carrying on after a failed lookup would take a duplicate checkpoint.
    $ErrorActionPreference = 'Stop'
    $m = Get-CreatedVmMachine -Name $Machine
    # Get-VMSnapshot / Remove-VMSnapshot: the *-VMCheckpoint names are missing from older Hyper-V modules (Windows 10).
    $old = @(Get-VMSnapshot -VMName $m.VMName | Where-Object { $_.Name -eq $Name })
    if ($old.Count -gt 0 -and -not $Replace) {
        throw "$($m.Name) already has a checkpoint named '$Name'. Add -Replace to take a new one in its place."
    }
    Checkpoint-VM -Name $m.VMName -SnapshotName $Name -ErrorAction Stop
    # Remove the old ones only once the new checkpoint exists, so a failure never leaves the VM without one.
    foreach ($c in $old) { Remove-VMSnapshot -VMSnapshot $c -Confirm:$false }
    if ($old.Count -gt 0) { Write-Step "Checkpoint '$Name' retaken on $($m.Name); the old one was deleted." }
    else { Write-Step "Checkpoint '$Name' taken on $($m.Name)." }
}

<#
.SYNOPSIS
    Restores a VM to a checkpoint, starts it, and waits until PowerShell Direct answers.
.DESCRIPTION
    WARNING: this discards the VM's kept changes and logs since the checkpoint, so collect logs first
    (Get-TermPointLog / Get-TestSections). Prints the guest clock offset afterwards.
.PARAMETER Machine
    A VM machine name.
.PARAMETER Checkpoint
    Checkpoint name (default Ready).
.NOTES
    Throws when not elevated, the VM or checkpoint does not exist, or two checkpoints share the name.
#>
function Restore-TestComputer {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [string]$Checkpoint = 'Ready')
    $m = Get-CreatedVmMachine -Name $Machine
    Remove-CachedSession -Name $m.Name
    # *-VMSnapshot, not *-VMCheckpoint: the latter names are missing from older Hyper-V modules (Windows 10).
    $snap = @(Get-VMSnapshot -VMName $m.VMName -ErrorAction Stop | Where-Object { $_.Name -eq $Checkpoint })
    if ($snap.Count -eq 0) { throw "$($m.Name) has no checkpoint named '$Checkpoint'." }
    if ($snap.Count -gt 1) { throw "$($m.Name) has $($snap.Count) checkpoints named '$Checkpoint'. Delete the extra ones in Hyper-V Manager first." }
    Restore-VMSnapshot -VMSnapshot $snap[0] -Confirm:$false -ErrorAction Stop
    if ((Get-TestVmState -Machine $m) -ne 'Running') { Start-VM -Name $m.VMName -ErrorAction Stop }
    Write-Step "$($m.Name) restored to '$Checkpoint'; waiting for it to answer..."
    $o = Wait-GuestAnswer -Machine $m -TimeoutSeconds 180
    if ($null -eq $o) { Write-Warning "$($m.Name) did not answer PowerShell Direct within 180 s." }
    else { Write-Step "$($m.Name) answers. Guest clock offset: $o s" }
}

<#
.SYNOPSIS
    Lists the configured machines with their state and TermPoint PIDs.
.DESCRIPTION
    State: 'local', the VM state ('not created' for a VM Get-VM cannot find, 'unknown (not elevated)' when the console
    cannot ask), 'disabled', or reachable/unreachable for WinRM. TermPoint PIDs are shown only where they can be read
    without a credential prompt.
.OUTPUTS
    Name, Kind, State, TermPoint.
.EXAMPLE
    Get-TestComputer
#>
function Get-TestComputer {
    [CmdletBinding()]
    param()
    $cfg = Get-TestConfig
    foreach ($raw in @($cfg.Machines)) {
        $m = Get-TestMachine -Name $raw.Name
        $state = ''; $pids = ''; $canCheck = $false
        switch ($m.Kind) {
            'Local' { $state = 'local'; $canCheck = $true }
            'VM' {
                if (-not (Test-ElevatedConsole)) { $state = 'unknown (not elevated)' }
                else {
                    $s = Get-TestVmState -Machine $m
                    if ($s -eq 'NotCreated') { $state = 'not created' } else { $state = $s.ToLowerInvariant() }
                    $canCheck = ($s -eq 'Running') -and (Test-CanConnectQuietly -Name $m.Name)
                    if ($s -eq 'Running' -and -not $canCheck) { $pids = '(not connected)' }
                }
            }
            'WinRM' {
                if (-not $m.Enabled) { $state = 'disabled' }
                else {
                    try { Test-WSMan -ComputerName $m.ComputerName -ErrorAction Stop | Out-Null; $state = 'reachable' } catch { $state = 'unreachable' }
                    $canCheck = ($state -eq 'reachable') -and (Test-CanConnectQuietly -Name $m.Name)
                    if ($state -eq 'reachable' -and -not $canCheck) { $pids = '(not connected)' }
                }
            }
        }
        if ($canCheck) {
            try { $pids = (@(Get-TermPointProcessOn -Name $m.Name | ForEach-Object { $_.Id }) -join ', ') } catch { $pids = '(error)' }
            if (-not $pids) { $pids = 'none' }
        }
        [pscustomobject]@{ Name = $m.Name; Kind = $m.Kind; State = $state; TermPoint = $pids }
    }
}

#endregion

#region ---------------------------------------------------------------- logs

<#
.SYNOPSIS
    Copies a machine's TermPoint log day files covering Since..now and returns the local paths.
.DESCRIPTION
    Day files "app-yyyy-MM-dd.log" live in %APPDATA%\TermPoint\Logs and, for MSIX installs, in
    %LOCALAPPDATA%\Packages\*TermPoint*\LocalCache\Roaming\TermPoint\Logs. Both are searched; per day the file
    written last wins. Copies go to "<WorkFolder>\logs-yyyyMMdd-HHmmss\<machine>\".
.PARAMETER Machine
    Machine name.
.PARAMETER Since
    Earliest time of interest (default: last reset, else 1 hour ago).
.OUTPUTS
    [string[]] local paths, oldest day first (empty when none were found).
.EXAMPLE
    Get-TermPointLog -Machine TPVM1 -Since (Get-Date).AddHours(-2)
#>
function Get-TermPointLog {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [datetime]$Since)
    $m = Get-TestMachine -Name $Machine
    $cfg = Get-TestConfig
    if (-not $PSBoundParameters.ContainsKey('Since')) { $Since = Get-DefaultSince }
    $remote = @(Invoke-TestComputer -Name $m.Name -ArgumentList @($Since) -ScriptBlock {
        param($since)
        $dirs = @(Join-Path $env:APPDATA 'TermPoint\Logs')
        $pk = Join-Path $env:LOCALAPPDATA 'Packages'
        if (Test-Path -LiteralPath $pk) {
            foreach ($d in @(Get-ChildItem -LiteralPath $pk -Directory -Filter '*TermPoint*' -ErrorAction SilentlyContinue)) {
                $dirs += Join-Path $d.FullName 'LocalCache\Roaming\TermPoint\Logs'
            }
        }
        for ($day = $since.Date; $day -le (Get-Date).Date; $day = $day.AddDays(1)) {
            $file = 'app-' + $day.ToString('yyyy-MM-dd') + '.log'
            $best = $dirs | ForEach-Object { Join-Path $_ $file } | Where-Object { Test-Path -LiteralPath $_ } |
                    ForEach-Object { Get-Item -LiteralPath $_ } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($best) { $best.FullName }
        }
    })
    $root = Join-Path $cfg.WorkFolder ('logs-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $dest = Join-Path $root $m.Name
    $out = @()
    foreach ($r in $remote) {
        $local = Join-Path $dest (Split-Path -Leaf $r)
        Copy-FromTestComputer -Name $m.Name -Path $r -Destination $local
        $out += $local
    }
    if ($out.Count -eq 0) { Write-Warning "No TermPoint log files found on $($m.Name) for $($Since.ToString('yyyy-MM-dd')) onwards." }
    return $out
}

<#
.SYNOPSIS
    Internal: parses TermPoint log files into entries.
.DESCRIPTION
    Format: "[2026-11-02 13:21:45.632] [INFO] [v1.2.3.0]" then "  Context : text"; further non-blank lines after the
    header (exception, stack) become Detail. Levels seen: INFO, WARN, ERROR, REPORT.
.PARAMETER Path
    Log file(s), read as UTF-8.
.OUTPUTS
    Objects: Time, Level, Version, Text, Detail.
#>
function Read-TermPointLogEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Path)
    $header = '^\[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] \[(?<lvl>[A-Z]+)\] \[v(?<ver>[^\]]*)\]'
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($file in $Path) {
        $cur = $null
        $detail = $null
        foreach ($line in [IO.File]::ReadAllLines($file, [Text.Encoding]::UTF8)) {
            if ($line -match $header) {
                if ($cur) { $cur.Detail = $detail.ToString().TrimEnd() }
                $cur = [pscustomobject]@{
                    Time = [datetime]::ParseExact($Matches['ts'], 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture)
                    Level = $Matches['lvl']; Version = $Matches['ver']; Text = ''; Detail = ''
                }
                $detail = New-Object System.Text.StringBuilder
                $list.Add($cur)
            }
            elseif ($cur) {
                if ($cur.Text -eq '' -and $line -match '^\s+Context : (?<t>.*)$') { $cur.Text = $Matches['t'] }
                elseif ($line.Trim() -ne '') { [void]$detail.AppendLine($line) }
            }
        }
        if ($cur) { $cur.Detail = $detail.ToString().TrimEnd() }
    }
    return $list.ToArray()
}

<# Internal: finds and loads a scenario file by name (scenarios\<name>.psd1) or path. #>
function Get-TestScenario {
    param([Parameter(Mandatory)][string]$Scenario)
    $dir = Join-Path $PSScriptRoot 'scenarios'
    if (Test-Path -LiteralPath $Scenario -PathType Leaf) { $file = $Scenario }
    else { $file = Join-Path $dir ($Scenario + '.psd1') }
    if (-not (Test-Path -LiteralPath $file)) {
        $names = @(Get-ChildItem -LiteralPath $dir -Filter '*.psd1' -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName })
        throw "Scenario '$Scenario' not found. Known scenarios: $($names -join ', ')."
    }
    return (Import-PowerShellDataFile -LiteralPath $file)
}

<#
.SYNOPSIS
    Checks the TermPoint logs of the participating machines against a scenario file.
.DESCRIPTION
    Fetches each role's log (Get-TermPointLog), keeps entries from Since to Until, then checks the scenario's Expect
    list (per role, in order, each after the previous match for that role), its Forbid list, and (unless the scenario
    has AllowErrors) that there is no [ERROR] entry or UnobservedTaskException. Also lists the app versions per role and
    warns if a role shows more than one (stale-bits canary). Patterns are case-insensitive regexes on the entry text.
    Machine clocks may differ slightly; ordering is per role except in -Timeline, which merges by time.
.PARAMETER Scenario
    A name in scenarios\ or a path to a .psd1.
.PARAMETER Roles
    Maps each scenario role to a machine name, e.g. @{ Writer='TPVM1'; Reader='DEVPC' }.
.PARAMETER Since
    Start of the window (default: last reset, else 1 hour ago).
.PARAMETER Until
    End of the window (default: no limit).
.PARAMETER Timeline
    Also print all entries matching any Expect/Forbid pattern, from all roles, merged by time.
.OUTPUTS
    Object with Passed, Expectations, ForbiddenHits, Errors, Versions.
.EXAMPLE
    Test-TermPointLog -Scenario M4-B2-close-then-takeover -Roles @{ Writer='TPVM1'; Reader='DEVPC' } -Timeline
.NOTES
    Throws when the scenario is unknown or a role has no machine.
#>
function Test-TermPointLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Scenario,
        [Parameter(Mandatory)][hashtable]$Roles,
        [datetime]$Since,
        [datetime]$Until,
        [switch]$Timeline
    )
    $sc = Get-TestScenario -Scenario $Scenario
    if (-not $PSBoundParameters.ContainsKey('Since')) { $Since = Get-DefaultSince }
    foreach ($r in @($sc.Roles)) { if (-not $Roles.ContainsKey($r)) { throw "Scenario $($sc.Name) needs a machine for the role '$r' (use -Roles @{ $r='<machine>' })." } }

    # Entries per role: Time, Level, Version, Text, Detail, Role, Seq (to keep file order for equal times).
    $entries = @{}
    $versions = @{}
    $cache = @{}
    foreach ($role in @($sc.Roles)) {
        $mach = (Get-TestMachine -Name $Roles[$role]).Name
        if (-not $cache.ContainsKey($mach)) {
            $files = @(Get-TermPointLog -Machine $mach -Since $Since)
            if ($files.Count -gt 0) { $cache[$mach] = @(Read-TermPointLogEntry -Path $files) } else { $cache[$mach] = @() }
        }
        $seq = 0
        $mine = New-Object System.Collections.Generic.List[object]
        foreach ($e in $cache[$mach]) {
            if ($e.Time -lt $Since) { continue }
            if ($PSBoundParameters.ContainsKey('Until') -and $e.Time -gt $Until) { continue }
            $seq++
            $mine.Add([pscustomobject]@{ Time = $e.Time; Level = $e.Level; Version = $e.Version; Text = $e.Text; Detail = $e.Detail; Role = $role; Machine = $mach; Seq = $seq })
        }
        $entries[$role] = $mine.ToArray()
        $versions[$role] = @($mine | ForEach-Object { $_.Version } | Select-Object -Unique)
    }

    Write-Host "Scenario $($sc.Name): $($sc.Description)"
    Write-Host ("Window: {0} to {1}" -f $Since.ToString('yyyy-MM-dd HH:mm:ss'), $(if ($PSBoundParameters.ContainsKey('Until')) { $Until.ToString('yyyy-MM-dd HH:mm:ss') } else { 'now' }))
    foreach ($role in @($sc.Roles)) { Write-Host ("  {0} = {1}: {2} entries" -f $role, $Roles[$role], @($entries[$role]).Count) }
    Write-Host ''

    $passed = $true
    $expectResults = New-Object System.Collections.Generic.List[object]
    $nextIndex = @{}
    foreach ($role in @($sc.Roles)) { $nextIndex[$role] = 0 }
    foreach ($x in @($sc.Expect | Where-Object { $_ })) {
        $role = $x.Role
        $list = @($entries[$role])
        $hit = $null
        for ($i = $nextIndex[$role]; $i -lt $list.Count; $i++) {
            if ([regex]::IsMatch($list[$i].Text, $x.Pattern, 'IgnoreCase')) { $hit = $list[$i]; $nextIndex[$role] = $i + 1; break }
        }
        if ($hit) {
            $t = $hit.Text; if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '...' }
            Write-Host ("PASS  {0}  {1,-6}  {2}  [{3}]" -f $hit.Time.ToString('HH:mm:ss.fff'), $role, $x.Note, $t) -ForegroundColor Green
        }
        else {
            $passed = $false
            Write-Host ("MISS  {0,-12}  {1,-6}  {2}  (pattern: {3})" -f '-', $role, $x.Note, $x.Pattern) -ForegroundColor Red
        }
        $expectResults.Add([pscustomobject]@{ Role = $role; Note = $x.Note; Pattern = $x.Pattern; Matched = [bool]$hit; Time = $(if ($hit) { $hit.Time } else { $null }); Text = $(if ($hit) { $hit.Text } else { $null }) })
    }

    $forbidHits = New-Object System.Collections.Generic.List[object]
    foreach ($f in @($sc.Forbid | Where-Object { $_ })) {
        $roleList = @($sc.Roles)
        if ($f.Role -ne '*') { $roleList = @($f.Role) }
        foreach ($role in $roleList) {
            foreach ($e in @($entries[$role])) {
                if ([regex]::IsMatch($e.Text, $f.Pattern, 'IgnoreCase')) {
                    $passed = $false
                    $t = $e.Text; if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '...' }
                    Write-Host ("FAIL  {0}  {1,-6}  forbidden: {2}  [{3}]" -f $e.Time.ToString('HH:mm:ss.fff'), $role, $f.Note, $t) -ForegroundColor Red
                    $forbidHits.Add($e)
                }
            }
        }
    }

    $errorHits = New-Object System.Collections.Generic.List[object]
    $allowErrors = $false
    if ($sc.ContainsKey('AllowErrors')) { $allowErrors = [bool]$sc.AllowErrors }
    if (-not $allowErrors) {
        foreach ($role in @($sc.Roles)) {
            foreach ($e in @($entries[$role])) {
                if ($e.Level -eq 'ERROR' -or $e.Text -match 'UnobservedTaskException' -or $e.Detail -match 'UnobservedTaskException') {
                    $passed = $false
                    $t = $e.Text; if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '...' }
                    Write-Host ("FAIL  {0}  {1,-6}  error entry [{2}]  [{3}]" -f $e.Time.ToString('HH:mm:ss.fff'), $role, $e.Level, $t) -ForegroundColor Red
                    $errorHits.Add($e)
                }
            }
        }
    }

    Write-Host ''
    foreach ($role in @($sc.Roles)) {
        $v = @($versions[$role])
        if ($v.Count -eq 0) { Write-Host ("Versions  {0}: (no entries)" -f $role) }
        elseif ($v.Count -gt 1) { Write-Warning ("{0} shows more than one app version ({1}): stale bits?" -f $role, ($v -join ', ')) }
        else { Write-Host ("Versions  {0}: v{1}" -f $role, $v[0]) }
    }

    if ($Timeline) {
        Write-Host ''
        Write-Host 'Timeline (entries matching any Expect/Forbid pattern, merged by time):'
        $pats = @(@($sc.Expect | Where-Object { $_ }) + @($sc.Forbid | Where-Object { $_ }) | ForEach-Object { $_.Pattern })
        $merged = @(foreach ($role in @($sc.Roles)) { foreach ($e in @($entries[$role])) {
            foreach ($pt in $pats) { if ([regex]::IsMatch($e.Text, $pt, 'IgnoreCase')) { $e; break } }
        } }) | Sort-Object Time, Seq
        foreach ($e in $merged) {
            $t = $e.Text; if ($t.Length -gt 140) { $t = $t.Substring(0, 140) + '...' }
            Write-Host ("  {0}  {1,-6}  [{2}] {3}" -f $e.Time.ToString('MM-dd HH:mm:ss.fff'), $e.Role, $e.Level, $t)
        }
    }

    Write-Host ''
    if ($passed) { Write-Host 'PASS' -ForegroundColor Green } else { Write-Host 'FAIL' -ForegroundColor Red }
    [pscustomobject]@{ Scenario = $sc.Name; Passed = $passed; Expectations = $expectResults.ToArray(); ForbiddenHits = $forbidHits.ToArray(); Errors = $errorHits.ToArray(); Versions = $versions }
}

<#
.SYNOPSIS
    Fetches a machine's log and runs Show-SaveStall.ps1 on it (scenario M1: which save stage stalled).
.PARAMETER Machine
    The writer's machine.
.PARAMETER Since
    Entries from this time (default: last 10 minutes, like the script itself).
.PARAMETER HoleOpened
    When the black hole opened, for the "+N s" column.
.EXAMPLE
    Show-SaveStall -Machine TPVM1 -Since 12:40 -HoleOpened 12:41:03.250
.NOTES
    Throws when no log is found.
#>
function Show-SaveStall {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Machine, [datetime]$Since, [datetime]$HoleOpened)
    if (-not $PSBoundParameters.ContainsKey('Since')) { $Since = (Get-Date).AddMinutes(-10) }
    $files = @(Get-TermPointLog -Machine $Machine -Since $Since)
    if ($files.Count -eq 0) { throw "No log found on $Machine." }
    $splat = @{ Log = $files[-1]; Since = $Since }
    if ($PSBoundParameters.ContainsKey('HoleOpened')) { $splat['HoleOpened'] = $HoleOpened }
    & (Join-Path $PSScriptRoot 'Show-SaveStall.ps1') @splat
}

#endregion

Export-ModuleMember -Function Get-TestConfig, Connect-TestComputer, Invoke-TestComputer, Connect-TestShare,
    Reset-TestDatabase, Get-TestDatabaseState, Watch-TestLock, Get-TestSections, Set-TestLock, Edit-TestDatabaseAsColleague,
    Install-TermPoint, Start-TermPoint, Get-TermPointProcess, Stop-TermPoint, Suspend-TermPoint, Resume-TermPoint,
    Suspend-TestComputer, Checkpoint-TestComputer, Restore-TestComputer, Get-TestComputer,
    Get-TermPointLog, Test-TermPointLog, Show-SaveStall
