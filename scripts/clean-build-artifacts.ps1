param(
    [switch]$IncludeVs
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Cleaning build artifacts under: $root"

$targets = Get-ChildItem -Path $root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Sort-Object { $_.FullName.Length } -Descending

if (-not $targets) {
    Write-Host "No bin/obj folders found."
}

foreach ($dir in $targets) {
    if (-not (Test-Path -LiteralPath $dir.FullName)) {
        continue
    }

    Write-Host "Removing: $($dir.FullName)"
    try {
        attrib -R "$($dir.FullName)\*" /S /D 2>$null
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Warning "Failed to remove $($dir.FullName): $($_.Exception.Message)"
    }
}

if ($IncludeVs) {
    $vs = Join-Path $root ".vs"
    if (Test-Path -LiteralPath $vs) {
        Write-Host "Removing: $vs"
        try {
            attrib -R "$vs\*" /S /D 2>$null
            Remove-Item -LiteralPath $vs -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to remove .vs: $($_.Exception.Message)"
        }
    }
    else {
        Write-Host ".vs folder not found."
    }
}

Write-Host "Done."
Write-Host "If some folders were locked, stop debugging, close Visual Studio, stop Android Emulator, then run this script again."
