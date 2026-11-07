<#
.SYNOPSIS
    Creates a Hyper-V test computer for TermPoint's field tests (1.2.4 spec item 8.9), or a copy of a finished one.

.DESCRIPTION
    Run in an ELEVATED PowerShell window on the Hyper-V host. Two uses:

      New:   .\New-TestComputer.ps1 -Name TPVM1 -IsoPath E:\ISO\Win11_x64.iso
             Makes a Generation 2 VM ready for Windows 11: Secure Boot, a virtual TPM, 2 processors, dynamic memory
             (it starts at 4 GB because Windows 11 setup checks for 4 GB; Windows gives back what it doesn't use),
             a 64 GB disk that only grows as it fills, and the ISO in its DVD drive. You then install Windows by hand;
             the script prints the steps.

      Copy:  .\New-TestComputer.ps1 -Name TPVM2 -CopyFrom TPVM1
             Makes the second (third-computer) test VM from a finished first one, which must be shut down. The copy
             gets a new VM id and a new network address, loses the source's checkpoints (restoring one of those
             would bring back the source's computer name), and is renamed inside Windows to the new name. It asks
             once for the VM's Windows sign-in, for the rename.

    Both kinds: checkpoints are Standard (they hold the VM's memory, so restoring one brings back a signed-in,
    running computer); automatic checkpoints are off.

    Network: by default the VM uses Hyper-V's built-in 'Default Switch', which shares this computer's connection
    (NAT) and changes nothing on this computer's network. The VM may then not find the file server by name; its IP
    address works. To make the VM a real computer on the LAN instead, pass -SwitchName 'TermPoint test LAN': that
    external switch is created on first use, after you confirm, and this computer's network drops for a few seconds
    while Windows moves its settings to the new virtual adapter (undo: Remove-VMSwitch -Name 'TermPoint test LAN').
    An existing VM moves between switches with Connect-VMNetworkAdapter -VMName <name> -SwitchName <switch>.

    Nothing here touches TermPoint, the share or the test database.

.PARAMETER Name
    The VM's name in Hyper-V and, for a copy, the computer name it is given inside Windows (15 characters at most:
    TermPoint shows it in capitals as the lock holder).

.PARAMETER IsoPath
    New VM only: the Windows 11 ISO (from microsoft.com/software-download/windows11, "Download Windows 11 Disk
    Image (ISO) for x64 devices").

.PARAMETER CopyFrom
    Copy only: the finished, shut-down VM to copy.

.PARAMETER VmRoot
    Folder that holds every test VM (one subfolder each). Default D:\Hyper-V (an internal SSD; drive C is
    nearly full and drive E is a USB hard disk, far too slow for a VM).

.PARAMETER SwitchName
    The virtual switch to connect the VM to. Default 'Default Switch' (Hyper-V's built-in NAT switch: no change to
    this computer's network). Any switch that already exists is used as it is. A name that doesn't exist yet makes
    the script offer to create an EXTERNAL switch of that name on -NetAdapterName (or reuse the external switch
    already bridged to that adapter), e.g. -SwitchName 'TermPoint test LAN'.

.PARAMETER NetAdapterName
    Only for creating an external switch: the host's wired adapter to bridge (Get-NetAdapter lists them). Default
    'Ethernet'.

.PARAMETER ProcessorCount
    Virtual processors. Default 2 (Windows 11's minimum; this host has 4 threads).

.PARAMETER StartupMemoryBytes
    Memory at boot. Default 4GB (Windows 11 setup refuses less).

.PARAMETER MinimumMemoryBytes
    Least memory dynamic memory may shrink to. Default 2GB (Windows 11 with TermPoint pages heavily below that).

.PARAMETER MaximumMemoryBytes
    Most memory dynamic memory may grow to. Default 4GB. This host has 16 GB: two VMs at 4 GB leave it 8 GB.

.PARAMETER DiskBytes
    New VM only: the virtual disk's maximum size. Default 64GB (Windows 11's minimum); the file grows as it fills,
    about 20 GB after setup.

.OUTPUTS
    None. Prints what it did and the next steps.

.EXAMPLE
    .\New-TestComputer.ps1 -Name TPVM1 -IsoPath E:\ISO\Win11_x64.iso

.EXAMPLE
    .\New-TestComputer.ps1 -Name TPVM2 -CopyFrom TPVM1

.NOTES
    Throws (stops) when: the console is not elevated or Hyper-V is missing (the #Requires lines); a VM with that
    name already exists; the ISO or the source VM is missing; the source VM is not shut down; the adapter doesn't
    exist; you decline the switch creation; drive space is short; the copy can't be reached for the rename within
    5 minutes (the copy then exists, unrenamed, and the message says how to finish).
#>
#Requires -RunAsAdministrator
#Requires -Modules Hyper-V

[CmdletBinding(DefaultParameterSetName = 'New')]
param(
    [Parameter(Mandatory)][ValidateLength(1, 15)][string]$Name,
    [Parameter(Mandatory, ParameterSetName = 'New')][string]$IsoPath,
    [Parameter(Mandatory, ParameterSetName = 'Copy')][string]$CopyFrom,
    [string]$VmRoot = 'D:\Hyper-V',
    [string]$SwitchName = 'Default Switch',
    [string]$NetAdapterName = 'Ethernet',
    [int]$ProcessorCount = 2,
    [long]$StartupMemoryBytes = 4GB,
    [long]$MinimumMemoryBytes = 2GB,
    [long]$MaximumMemoryBytes = 4GB,
    [long]$DiskBytes = 64GB
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------

<#
.SYNOPSIS
    Returns the name of the external switch to use, creating it (after the user confirms) when there is none.
.PARAMETER Switch
    The preferred switch name.
.PARAMETER Adapter
    The host network adapter to bridge.
.OUTPUTS
    [string] the switch name.
.NOTES
    Throws when the adapter doesn't exist or the user declines.
#>
function Get-ExternalSwitch {
    param([string]$Switch, [string]$Adapter)

    $byName = Get-VMSwitch -Name $Switch -ErrorAction SilentlyContinue
    if ($byName) { return $byName.Name }

    $nic = Get-NetAdapter -Name $Adapter -ErrorAction SilentlyContinue
    if (-not $nic) { throw "There is no network adapter named '$Adapter'. Get-NetAdapter lists them; pass -NetAdapterName." }

    # An adapter can carry only one external switch: reuse one that is already bound to it.
    $bound = Get-VMSwitch -SwitchType External -ErrorAction SilentlyContinue |
             Where-Object { $_.NetAdapterInterfaceDescription -eq $nic.InterfaceDescription } |
             Select-Object -First 1
    if ($bound) {
        Write-Host "Using the external switch '$($bound.Name)', already bridged to '$Adapter'."
        return $bound.Name
    }

    Write-Warning ("Next: create the external switch '$Switch' on '$Adapter'. This computer's network drops for a few " +
                   "seconds while Windows moves its settings to the new virtual adapter. Close TermPoint and anything " +
                   "else using the network share first.")
    $answer = Read-Host "Type Y to create the switch now"
    if ($answer -ne 'Y') { throw 'Stopped before creating the switch. Nothing was changed.' }

    New-VMSwitch -Name $Switch -NetAdapterName $Adapter -AllowManagementOS $true | Out-Null
    Write-Host "Created the external switch '$Switch'."
    return $Switch
}

<#
.SYNOPSIS
    Throws unless the drive holding a folder has at least the given free space.
.PARAMETER Folder
    Any path on the drive to check (it need not exist yet).
.PARAMETER NeededBytes
    The free space required.
#>
function Assert-FreeSpace {
    param([string]$Folder, [long]$NeededBytes)

    $driveName = (Split-Path -Path $Folder -Qualifier).TrimEnd(':')
    $drive     = Get-PSDrive -Name $driveName
    if ($drive.Free -lt $NeededBytes) {
        throw ("Drive $($driveName): has {0:N0} GB free; this needs about {1:N0} GB." -f ($drive.Free / 1GB), ($NeededBytes / 1GB))
    }
}

<#
.SYNOPSIS
    Applies the settings every test VM shares: processors, dynamic memory, checkpoint behaviour, start/stop actions.
.PARAMETER VmName
    The VM to configure.
#>
function Set-TestVmSettings {
    param([string]$VmName)

    Set-VM -Name $VmName -ProcessorCount $ProcessorCount -DynamicMemory `
           -MemoryMinimumBytes $MinimumMemoryBytes -MemoryMaximumBytes $MaximumMemoryBytes `
           -AutomaticCheckpointsEnabled $false -CheckpointType Standard `
           -AutomaticStartAction Nothing -AutomaticStopAction Save
}

<#
.SYNOPSIS
    Gives a VM's network adapter a new random Hyper-V MAC address (00-15-5D-xx-xx-xx), so a copy never shares the
    source's address on the LAN.
.PARAMETER VmName
    The VM whose adapter gets the new address.
#>
function Set-NewMacAddress {
    param([string]$VmName)

    $bytes = New-Object byte[] 3
    (New-Object System.Random).NextBytes($bytes)
    $mac = '00155D' + (($bytes | ForEach-Object { $_.ToString('X2') }) -join '')
    Set-VMNetworkAdapter -VMName $VmName -StaticMacAddress $mac
    Write-Host "Network address: $mac"
}

# ---------------------------------------------------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------------------------------------------------

if (Get-VM -Name $Name -ErrorAction SilentlyContinue) {
    throw "A VM named '$Name' already exists. Pick another name, or remove that VM in Hyper-V Manager first."
}

$vmFolder = Join-Path $VmRoot $Name
$vhdDir   = Join-Path $vmFolder 'Virtual Hard Disks'

# ---------------------------------------------------------------------------------------------------------------------
# New VM
# ---------------------------------------------------------------------------------------------------------------------

if ($PSCmdlet.ParameterSetName -eq 'New') {
    if (-not (Test-Path -LiteralPath $IsoPath)) { throw "The ISO '$IsoPath' doesn't exist." }
    Assert-FreeSpace -Folder $VmRoot -NeededBytes 30GB
    $switch = Get-ExternalSwitch -Switch $SwitchName -Adapter $NetAdapterName

    New-VM -Name $Name -Generation 2 -MemoryStartupBytes $StartupMemoryBytes -Path $VmRoot -SwitchName $switch `
           -NewVHDPath (Join-Path $vhdDir "$Name.vhdx") -NewVHDSizeBytes $DiskBytes | Out-Null
    Set-TestVmSettings -VmName $Name
    Set-VMFirmware -VMName $Name -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows

    # Windows 11 wants TPM 2.0. The VM gets a virtual one, protected by a key on this computer (no physical TPM needed).
    $tpmOn = $true
    try {
        Set-VMKeyProtector -VMName $Name -NewLocalKeyProtector
        Enable-VMTPM -VMName $Name
    }
    catch {
        $tpmOn = $false
        Write-Warning "The virtual TPM could not be turned on: $($_.Exception.Message)"
    }

    Add-VMDvdDrive -VMName $Name -Path $IsoPath
    Set-VMFirmware -VMName $Name -FirstBootDevice (Get-VMDvdDrive -VMName $Name)

    Write-Host ''
    Write-Host "Created $Name in $vmFolder (TPM: $(if ($tpmOn) { 'on' } else { 'OFF, see step 2' }))." -ForegroundColor Green
    Write-Host @"

Next, by hand:
 1. Open its window:  vmconnect.exe localhost $Name   then Action > Start. When it says
    "Press any key to boot from CD or DVD", press a key (it waits only a few seconds; if you miss it, Action > Reset).
 2. Windows Setup: your language, "I don't have a product key", Windows 11 Pro, Custom install on Drive 0.
"@
    if (-not $tpmOn) {
        Write-Host @"
    Without the virtual TPM, setup refuses this computer. At the FIRST setup screen press Shift+F10 and type these
    three lines, then close the window and carry on:
      reg add HKLM\SYSTEM\Setup\LabConfig /v BypassTPMCheck /t REG_DWORD /d 1 /f
      reg add HKLM\SYSTEM\Setup\LabConfig /v BypassSecureBootCheck /t REG_DWORD /d 1 /f
      reg add HKLM\SYSTEM\Setup\LabConfig /v BypassRAMCheck /t REG_DWORD /d 1 /f
"@
    }
    Write-Host @"
 3. Account: for a local account, at the sign-in screen press Shift+F10 and type  start ms-cxh:localonly
    If that doesn't work, a Microsoft account is fine too. Either way give it a password: the field-test console signs
    in to the VM with it (PowerShell Direct can't use an account without one). Same time zone as this computer (the
    logs are in local time).
 4. In the VM: File Explorer > address bar  \\<file server>\<share>  > sign in with the file server's account and tick
    "Remember my credentials". On the Default Switch the VM may not find the server by name: use its IP address
    (Resolve-DnsName <server> on this computer shows it), or move the VM to an external switch (see the help).
 5. Back here, in the field-test console (see tools\FieldTest\README.md): install TermPoint, start it, choose the test
    database, close it. Then remove the ISO and take the clean-state checkpoint:
      Set-VMDvdDrive -VMName $Name -Path `$null
      Checkpoint-VM -Name $Name -SnapshotName Ready
"@
    return
}

# ---------------------------------------------------------------------------------------------------------------------
# Copy of a finished VM
# ---------------------------------------------------------------------------------------------------------------------

$source = Get-VM -Name $CopyFrom -ErrorAction SilentlyContinue
if (-not $source) { throw "There is no VM named '$CopyFrom'." }
if ($source.State -ne 'Off') {
    throw "Shut $CopyFrom down first (Start > Shut down inside it, or Stop-VM -Name $CopyFrom). It is $($source.State)."
}

# The source's whole folder (disks, checkpoints and their saved memory) is exported, then imported again.
$sourceBytes = (Get-ChildItem -LiteralPath $source.Path -Recurse -File | Measure-Object -Property Length -Sum).Sum
Assert-FreeSpace -Folder $VmRoot -NeededBytes (2 * $sourceBytes + 5GB)

# Export-VM + Import-VM -Copy keeps the virtual TPM's key, so a disk the TPM unlocks still opens in the copy.
$exportRoot = Join-Path $VmRoot "_export-for-$Name"
Write-Host "Exporting $CopyFrom (a few minutes)..."
Export-VM -Name $CopyFrom -Path $exportRoot
$vmcx = Get-ChildItem -Path (Join-Path (Join-Path $exportRoot $CopyFrom) 'Virtual Machines') -Filter '*.vmcx' |
        Select-Object -First 1
Write-Host 'Importing the copy...'
$copy = Import-VM -Path $vmcx.FullName -Copy -GenerateNewId -VirtualMachinePath $vmFolder `
                  -VhdDestinationPath $vhdDir -SnapshotFilePath (Join-Path $vmFolder 'Snapshots') `
                  -SmartPagingFilePath $vmFolder
Rename-VM -VM $copy -NewName $Name
Remove-Item -LiteralPath $exportRoot -Recurse -Force   # only the export this script just made

# Restoring a checkpoint inherited from the source would undo the rename below: drop them all (Hyper-V merges them).
Get-VMSnapshot -VMName $Name | Remove-VMSnapshot
Set-TestVmSettings -VmName $Name
Set-NewMacAddress -VmName $Name

Start-VM -Name $Name
$cred = Get-Credential -Message "Windows sign-in on $CopyFrom (the copy has the same accounts), to rename the copy $Name"

# Wait for Windows in the copy to answer PowerShell Direct (it boots for a minute or two).
$deadline = (Get-Date).AddMinutes(5)
$answered = $false
while (-not $answered -and (Get-Date) -lt $deadline) {
    try {
        Invoke-Command -VMName $Name -Credential $cred -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop | Out-Null
        $answered = $true
    }
    catch { Start-Sleep -Seconds 5 }
}
if (-not $answered) {
    throw ("$Name was copied and started, but Windows in it didn't answer within 5 minutes, so it still has the name " +
           "$CopyFrom. Once it is up, rename it inside Windows (Settings > System > About > Rename this PC) to $Name.")
}

Invoke-Command -VMName $Name -Credential $cred -ArgumentList $Name -ScriptBlock {
    param($newName)
    Rename-Computer -NewName $newName -Force -Restart
}

Write-Host ''
Write-Host "Created $Name as a copy of $CopyFrom; it is restarting under its new name." -ForegroundColor Green
Write-Host @"

Next:
 1. vmconnect.exe localhost $Name : sign in; check the share still opens in File Explorer.
 2. Start TermPoint there once (it opens the test database) and close it, so its title and lock show $Name.
 3. Take its own clean-state checkpoint:  Checkpoint-VM -Name $Name -SnapshotName Ready
 Two VMs at 4 GB each leave this computer 8 of its 16 GB.
"@
