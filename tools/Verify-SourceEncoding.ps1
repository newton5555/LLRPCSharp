[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoots = @(
    (Join-Path $repositoryRoot 'src'),
    (Join-Path $repositoryRoot 'tests'),
    (Join-Path $repositoryRoot 'samples'),
    (Join-Path $repositoryRoot 'tools')
)

$files = foreach ($sourceRoot in $sourceRoots) {
    if (Test-Path -LiteralPath $sourceRoot) {
        Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }
}

$invalidFiles = foreach ($file in $files) {
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $prefix = [byte[]]::new(3)
        $read = $stream.Read($prefix, 0, $prefix.Length)
        if ($read -lt 3 -or
            $prefix[0] -ne 0xEF -or
            $prefix[1] -ne 0xBB -or
            $prefix[2] -ne 0xBF) {
            $file.FullName
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ($invalidFiles.Count -ne 0) {
    Write-Error (
        "The following C# source files are not UTF-8 with BOM:`n" +
        ($invalidFiles -join [Environment]::NewLine))
}

Write-Host "Verified UTF-8 BOM for $($files.Count) C# source files."
