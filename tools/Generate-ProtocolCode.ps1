[CmdletBinding()]
param(
    [ValidateSet('All', '1.0.1', '1.1', 'Impinj', '2.0')]
    [string]$Target = 'All',

    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Set-Location $repositoryRoot

$verifyArg = if ($Verify) { @('--verify') } else { @() }

function Invoke-Generator {
    param(
        [string]$Name,
        [string[]]$GeneratorArgs
    )

    Write-Host "=== Generating $Name Code ===" -ForegroundColor Green
    $cmdArgs = @('run', '--project', 'src/LlrpNet.ProtocolGenerator.Tool', '--') + $GeneratorArgs + $verifyArg
    
    & dotnet @cmdArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Protocol generation for '$Name' failed with exit code $LASTEXITCODE"
    }
}

if ($Target -eq 'All' -or $Target -eq '1.0.1') {
    Invoke-Generator -Name "LLRP 1.0.1" -GeneratorArgs @(
        '--input', 'definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml',
        '--output', 'src/LlrpNet.Protocol',
        '--root-namespace', 'LlrpNet.Protocol',
        '--version-namespace', 'V1_0_1',
        '--protocol-version', '1',
        '--codecs'
    )
}

if ($Target -eq 'All' -or $Target -eq '1.1') {
    Invoke-Generator -Name "LLRP 1.1" -GeneratorArgs @(
        '--input', 'definitions/llrp-1.1.yaml',
        '--base', 'definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml',
        '--output', 'src/LlrpNet.Protocol',
        '--root-namespace', 'LlrpNet.Protocol',
        '--version-namespace', 'V1_1',
        '--protocol-version', '2',
        '--registry-module-name', 'Llrp11StandardModule',
        '--codecs'
    )
}

if ($Target -eq 'All' -or $Target -eq 'Impinj') {
    Invoke-Generator -Name "Impinj Extension" -GeneratorArgs @(
        '--input', 'definitions/imports/xml/extensions/impinj/Impinjdef.xml',
        '--dependency', 'definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml',
        '--dependency-root-namespace', 'LlrpNet.Protocol',
        '--output', 'src/LlrpSdk.Extensions.Impinj',
        '--root-namespace', 'LlrpSdk.Extensions.Impinj',
        '--version-namespace', 'V1_0_1',
        '--protocol-version', '1',
        '--registry-module-name', 'ImpinjProtocolModule',
        '--codecs'
    )
}

if ($Target -eq '2.0') {
    Invoke-Generator -Name "LLRP 2.0" -GeneratorArgs @(
        '--input', 'definitions/llrp-2.0-delta.yaml',
        '--base', 'definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml',
        '--base', 'definitions/llrp-1.1.yaml',
        '--output', 'src/LlrpNet.Protocol',
        '--root-namespace', 'LlrpNet.Protocol',
        '--version-namespace', 'V2_0',
        '--protocol-version', '3',
        '--registry-module-name', 'Llrp20StandardModule',
        '--codecs'
    )
}

Write-Host "Protocol code generation finished successfully." -ForegroundColor Cyan
