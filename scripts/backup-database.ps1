<#
.SYNOPSIS
    Backs up one OnePondy PostgreSQL database and the server's roles, checks the backup can be read, and keeps only the newest few.

.DESCRIPTION
    Writes two files per run into BackupDir, named with the same timestamp:
      <database>-<yyyyMMdd-HHmmss>.dump         the database and the grants on its tables (pg_dump custom format, restored with pg_restore)
      <database>-<yyyyMMdd-HHmmss>.globals.sql  the server's roles (pg_dumpall --globals-only), which must exist before the grants can apply

    The password is never a parameter and never stored here. Set PGPASSWORD for the run, or put the login in the pgpass file
    (%APPDATA%\postgresql\pgpass.conf). The script never prompts, so it is safe in Task Scheduler.

    A backup counts only if the file is not empty and pg_restore can read its contents. Until then it is a ".partial" file, and older
    backups are deleted only after the new one passes, so a failed run never costs you the backups you already had.

    The globals file holds password hashes. Its permissions are restricted to you, SYSTEM and Administrators; keep it out of shared folders.

    Restoring is described in docs/backup-and-restore.md.

.EXAMPLE
    $env:PGPASSWORD = '...'; .\scripts\backup-database.ps1 -Database onepondy_dev -BackupDir D:\Backups\OnePondy -Keep 14
#>
[CmdletBinding()]
param(
    [string]$Database = 'onepondy_dev',
    [string]$HostName = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    [string]$BackupDir = 'C:\OnePondyBackups',
    # How many backups to keep (each is one .dump and one .globals.sql).
    [ValidateRange(1, 365)]
    [int]$Keep = 7,
    [string]$BinDir = 'C:\Program Files\PostgreSQL\18\bin'
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $BackupDir 'backup.log'

function Write-Log([string]$message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $message
    Write-Host $line
    if (Test-Path $BackupDir) { Add-Content -Path $log -Value $line }
}

# Runs a PostgreSQL tool and stops with its own message if it fails.
function Invoke-Tool([string]$exe, [string[]]$arguments) {
    # Windows PowerShell 5.1 would treat any text a tool writes to stderr as an error; only the exit code decides here.
    $ErrorActionPreference = 'Continue'
    $output = & (Join-Path $BinDir $exe) @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($output -join ' ')" }
}

$partials = @()
try {
    foreach ($tool in 'pg_dump.exe', 'pg_dumpall.exe', 'pg_restore.exe') {
        if (-not (Test-Path (Join-Path $BinDir $tool))) { throw "$tool was not found in $BinDir. Pass -BinDir." }
    }
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dump = Join-Path $BackupDir "$Database-$stamp.dump"
    $globals = Join-Path $BackupDir "$Database-$stamp.globals.sql"
    $connection = @('-w', '-h', $HostName, '-p', $Port, '-U', $User)

    Write-Log "Backing up $Database on ${HostName}:$Port as $User"

    $partials += "$dump.partial"
    Invoke-Tool 'pg_dump.exe' ($connection + @('-d', $Database, '-Fc', '-f', "$dump.partial"))
    if ((Get-Item "$dump.partial").Length -eq 0) { throw 'The dump is empty.' }
    # Reading the table of contents proves the file is a valid archive; it does not touch any database.
    Invoke-Tool 'pg_restore.exe' @('--list', "$dump.partial") | Out-Null

    $partials += "$globals.partial"
    Invoke-Tool 'pg_dumpall.exe' ($connection + @('--globals-only', '-f', "$globals.partial"))
    if ((Get-Item "$globals.partial").Length -eq 0) { throw 'The roles file is empty.' }
    try {
        & icacls.exe "$globals.partial" /inheritance:r /grant:r "${env:USERDOMAIN}\${env:USERNAME}:F" '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls exit $LASTEXITCODE" }
    }
    catch {
        Write-Log "WARNING: could not restrict the roles file's permissions ($($_.Exception.Message)). Protect it yourself."
    }

    Move-Item -Path "$dump.partial" -Destination $dump
    Move-Item -Path "$globals.partial" -Destination $globals
    $partials = @()
    Write-Log ('OK  {0} ({1:N0} KB) and {2}' -f (Split-Path $dump -Leaf), ((Get-Item $dump).Length / 1KB), (Split-Path $globals -Leaf))

    # Only now, with a good new backup in place, remove the oldest ones. Only files made by this script are touched.
    $escaped = [regex]::Escape($Database)
    foreach ($suffix in 'dump', 'globals\.sql') {
        $files = Get-ChildItem -Path $BackupDir -File |
            Where-Object { $_.Name -match "^$escaped-\d{8}-\d{6}\.$suffix$" } |
            Sort-Object Name -Descending
        foreach ($old in ($files | Select-Object -Skip $Keep)) {
            [IO.File]::Delete($old.FullName)
            Write-Log "Removed old backup $($old.Name)"
        }
    }
    exit 0
}
catch {
    foreach ($partial in $partials) {
        if (Test-Path $partial) { [IO.File]::Delete($partial) }
    }
    Write-Log "FAILED: $($_.Exception.Message)"
    exit 1
}
