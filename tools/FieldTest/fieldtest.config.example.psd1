# Example configuration for the FieldTest module.
# Copy this file to fieldtest.config.psd1 (beside FieldTest.psm1) and edit it, or point $env:FIELDTEST_CONFIG at
# another file. Everything the module needs to know about YOUR machines, share and user lives here; none of it is
# hardcoded in the module. ASCII only, please (Windows PowerShell 5.1 misreads BOM-less UTF-8).
@{
    # D: the shared TEST database. Never point this at a real department's database.
    Database   = '\\server\share\termpointdata\BIOL-TT.db'

    # A pristine copy of D beside it. Reset-TestDatabase copies it over D.
    Master     = '\\server\share\termpointdata\BIOL-TT-master.db'

    # Optional: full SHA-256 of Master. Reset-TestDatabase warns when Master no longer matches it. '' = don't check.
    MasterHash = ''

    # Used by Connect-TestShare: runs `net use <ShareRoot> /user:<ShareUser>` (net use asks for the password).
    ShareRoot  = '\\server\share'
    ShareUser  = 'SERVER\user'

    # Local folder (not on the share) for copies of D, collected logs, lock-watch output and run notes.
    WorkFolder = 'D:\fieldtest'

    # How to run Python for the helper scripts in python\. -I = isolated mode.
    PythonExe  = 'py'
    PythonArgs = @('-3.11', '-I')

    # Identity name of the sideloaded MSIX package; the app id inside it is 'TermPoint'.
    MsixPackageName = 'AcademicSolutions.TermPoint'

    # Optional: launch this exe instead of the MSIX (a non-packaged build). '' = use the MSIX.
    ExePath    = ''

    # The computers that take part. Kinds:
    #   Local - this computer; commands run in-process.
    #   VM    - a Hyper-V guest on this computer, reached by PowerShell Direct (needs an elevated console).
    #           A VM that Get-VM cannot find is treated as "not created yet" and skipped.
    #   WinRM - another computer, reached by New-PSSession -ComputerName. Enabled = $false skips it everywhere.
    Machines   = @(
        @{ Name = 'DEVPC';  Kind = 'Local' }
        @{ Name = 'TPVM1';  Kind = 'VM';    VMName = 'TPVM1' }
        @{ Name = 'LAPTOP'; Kind = 'WinRM'; ComputerName = 'LAPTOP'; Enabled = $false }
    )
}
