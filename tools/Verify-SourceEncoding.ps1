[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
# Generated protocol sources use UTF-8 without a BOM. This is the same
# representation emitted by ProtocolGenerator.Tool and requested by
# .editorconfig (`charset = utf-8`). Handwritten legacy files are intentionally
# outside this check so a one-time encoding cleanup does not mix with protocol
# generation changes.
$files = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*.g.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

$invalidFiles = foreach ($file in $files) {
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $prefix = [byte[]]::new(3)
        $read = $stream.Read($prefix, 0, $prefix.Length)
        if ($read -ge 3 -and
            $prefix[0] -eq 0xEF -and
            $prefix[1] -eq 0xBB -and
            $prefix[2] -eq 0xBF) {
            $file.FullName
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ($invalidFiles.Count -ne 0) {
    Write-Error (
        "The following generated C# source files contain a UTF-8 BOM:`n" +
            ($invalidFiles -join [Environment]::NewLine))
}

$invalidLineEndingFiles = foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasBareLineFeed = $false
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 0x0A -and ($index -eq 0 -or $bytes[$index - 1] -ne 0x0D)) {
            $hasBareLineFeed = $true
            break
        }
    }

    if ($hasBareLineFeed) {
        $file.FullName
    }
}

if ($invalidLineEndingFiles.Count -ne 0) {
    Write-Error (
        "The following generated C# source files do not use CRLF line endings:`n" +
            ($invalidLineEndingFiles -join [Environment]::NewLine))
}

Write-Host "Verified UTF-8 without BOM and CRLF line endings for $($files.Count) generated C# source files."
