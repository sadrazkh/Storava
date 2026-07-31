<#
.SYNOPSIS
    Checks that a built MSI still installs per-user, and carries the version it was asked for.

.DESCRIPTION
    WiX's Scope="perUser" did not, on its own, put ALLUSERS and MSIINSTALLPERUSER into the Property
    table. Without them Windows Installer registers the product machine-wide while writing its
    files into one user's profile — installed for everybody, present for nobody but one.

    That failure is invisible until someone inspects the registry after installing, so it is read
    back out of the built package here instead.

    Per-user is not a preference. The Agent's identity and channel secret are DPAPI-encrypted for
    one Windows account; an Agent installed machine-wide and started as anyone else could not
    decrypt them.

    The version check lives here too, rather than in a second copy of this COM code inside the
    release workflow. Two implementations of the same awkward thing is one more than can be kept
    right, and the workflow's copy is the one that failed a release while this script sat beside it
    doing the same reads correctly.

.PARAMETER MsiPath
    The package to inspect.

.PARAMETER ExpectedVersion
    Three-part version the build was asked for, without the MSI's trailing field — pass 0.1.0 to
    require a ProductVersion of 0.1.0.0. Omitted, the version is reported but not enforced.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MsiPath,

    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'

# Surfaced as a GitHub annotation as well as thrown, so a failed release says what went wrong on
# the run's summary page rather than only inside a log somebody has to go and open.
function Fail([string] $message) {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error title=Installer check::$message"
    }

    throw $message
}

if (-not (Test-Path $MsiPath)) {
    # Named separately from a version mismatch: a missing package means an earlier step did not
    # produce what it claimed to, which is a different fault from one that produced the wrong thing.
    Fail "No MSI at $MsiPath. The build step before this one did not produce it."
}

function Get-MsiProperties([string] $path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer

    # Called directly rather than through Type.InvokeMember. Both work in Windows PowerShell, and
    # the reflection form is the sort of thing that behaves differently once a PSObject wrapper is
    # in the middle of it — which is the only difference between the shell this was written against
    # and the pwsh the build agent runs.
    $database = $installer.OpenDatabase((Resolve-Path $path).Path, 0)
    $view = $database.OpenView('SELECT Property, Value FROM Property')

    # Discarded, not just ignored: Execute puts a null on the output stream, and a function that
    # leaks one returns an array with the hashtable as its second element instead of the hashtable.
    $null = $view.Execute()

    $properties = @{}
    while ($true) {
        $record = $view.Fetch()
        if ($null -eq $record) { break }

        $properties[$record.StringData(1)] = $record.StringData(2)
    }

    return $properties
}

try {
    $properties = Get-MsiProperties $MsiPath
}
catch {
    Fail "Could not read $MsiPath : $($_.Exception.Message)"
}

# ALLUSERS must be absent. Present and set to 2 it means "per-machine if this user is allowed",
# which on an administrator's machine silently becomes a machine-wide install.
if ($properties.ContainsKey('ALLUSERS')) {
    Fail "$MsiPath sets ALLUSERS='$($properties['ALLUSERS'])'; it must be absent for a per-user install."
}

if ($properties['MSIINSTALLPERUSER'] -ne '1') {
    Fail "$MsiPath does not declare MSIINSTALLPERUSER=1."
}

$found = $properties['ProductVersion']

if ($ExpectedVersion) {
    # MSI keeps four fields; only the first three decide upgrades, and the build appends the fourth.
    $want = "$ExpectedVersion.0"
    if ($found -ne $want) {
        Fail "$MsiPath is version '$found' but the tag asks for '$want'."
    }
}

$size = [math]::Round((Get-Item $MsiPath).Length / 1MB)
"$([System.IO.Path]::GetFileName($MsiPath)): per-user, $($properties['ProductName']) $found, $size MB"
