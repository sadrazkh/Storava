<#
.SYNOPSIS
    Checks that a built MSI still installs per-user.

.DESCRIPTION
    WiX's Scope="perUser" did not, on its own, put ALLUSERS and MSIINSTALLPERUSER into the Property
    table. Without them Windows Installer registers the product machine-wide while writing its
    files into one user's profile — installed for everybody, present for nobody but one.

    That failure is invisible until someone inspects the registry after installing, so it is read
    back out of the built package here instead.

    Per-user is not a preference. The Agent's identity and channel secret are DPAPI-encrypted for
    one Windows account; an Agent installed machine-wide and started as anyone else could not
    decrypt them.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MsiPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $MsiPath)) {
    throw "No MSI at $MsiPath"
}

function Get-MsiProperties([string] $path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember(
        'OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))
    $view = $database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $database, @('SELECT Property, Value FROM Property'))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null

    $properties = @{}
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) { break }

        $name = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
        $value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2))
        $properties[$name] = $value
    }

    return $properties
}

$properties = Get-MsiProperties $MsiPath

# ALLUSERS must be absent. Present and set to 2 it means "per-machine if this user is allowed",
# which on an administrator's machine silently becomes a machine-wide install.
if ($properties.ContainsKey('ALLUSERS')) {
    throw "$MsiPath sets ALLUSERS='$($properties['ALLUSERS'])'; it must be absent for a per-user install."
}

if ($properties['MSIINSTALLPERUSER'] -ne '1') {
    throw "$MsiPath does not declare MSIINSTALLPERUSER=1."
}

$size = [math]::Round((Get-Item $MsiPath).Length / 1MB)
"$([System.IO.Path]::GetFileName($MsiPath)): per-user, $($properties['ProductName']) $($properties['ProductVersion']), $size MB"
