$ErrorActionPreference = 'Stop'
$flagRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$flagBase = Join-Path $flagRoot 'artifacts/flag-file-followup-20261022'
$flagPackage = Join-Path $flagBase 'download/TMIS_CustomFlags_Installation_20261022'
$flagInstaller = Join-Path $flagPackage 'Install.ps1'
$flagGame = 'C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania'
$flagManifest = Get-Content -Raw (Join-Path $flagPackage 'manifest.json') | ConvertFrom-Json
$flagEvents = @()
if (Get-Process Trackmania -ErrorAction SilentlyContinue) { throw 'Close game before package test' }

function Assert-FlagFiles([bool]$Present, [bool]$ItemsOnly = $false) {
    foreach ($flagRow in $flagManifest.files) {
        $flagPath = Join-Path $flagGame $flagRow.path
        $flagExpected = $Present -and (-not $ItemsOnly -or $flagRow.path.EndsWith('.Item.Gbx'))
        if ((Test-Path -LiteralPath $flagPath) -ne $flagExpected) { throw "Unexpected file presence: $flagPath" }
        if ($flagExpected -and (Get-FileHash -Algorithm SHA256 -LiteralPath $flagPath).Hash -ne $flagRow.sha256) {
            throw "Unexpected installed bytes: $flagPath"
        }
    }
}

$flagTokens = $null; $flagErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($flagInstaller,[ref]$flagTokens,[ref]$flagErrors) | Out-Null
if ($flagErrors.Count) { throw 'PowerShell parser rejected installer' }
$flagEvents += 'Installer PowerShell AST passed'
Assert-FlagFiles $false
& $flagInstaller -GameDirectory $flagGame
Assert-FlagFiles $true
$flagEvents += 'Initial default install created all four exact files'
& $flagInstaller -GameDirectory $flagGame -Uninstall -WhatIf
Assert-FlagFiles $true
$flagEvents += 'Uninstall WhatIf preserved all four exact accepted files'
& $flagInstaller -GameDirectory $flagGame -Uninstall
Assert-FlagFiles $false
$flagEvents += 'Uninstall removed the four unchanged files'
& $flagInstaller -GameDirectory $flagGame -WhatIf
Assert-FlagFiles $false
$flagEvents += 'Install WhatIf created no files'
& $flagInstaller -GameDirectory $flagGame
Assert-FlagFiles $true
$flagEvents += 'Install created all four exact accepted files'
$flagRefused = $false
try { & $flagInstaller -GameDirectory $flagGame } catch {
    if ($_.Exception.Message -notlike '*Refusing to overwrite an existing loose file*') { throw }
    $flagRefused = $true
}
if (-not $flagRefused) { throw 'Repeated install should refuse existing targets' }
Assert-FlagFiles $true
$flagEvents += 'Repeated installation refused without changing any file'
& $flagInstaller -GameDirectory $flagGame -Uninstall
Assert-FlagFiles $false
$flagEvents += 'Final uninstall passed; no custom flag installation file remains live'
& $flagInstaller -GameDirectory $flagGame -ItemsOnly -WhatIf
Assert-FlagFiles $false
& $flagInstaller -GameDirectory $flagGame -ItemsOnly
Assert-FlagFiles $true $true
$flagEvents += 'ItemsOnly WhatIf and install passed; only three Item.Gbx files created, inventory JSON absent'
& $flagInstaller -GameDirectory $flagGame -ItemsOnly -Uninstall -WhatIf
Assert-FlagFiles $true $true
& $flagInstaller -GameDirectory $flagGame -ItemsOnly -Uninstall
Assert-FlagFiles $false
$flagEvents += 'ItemsOnly uninstall WhatIf preserved the files; actual uninstall removed exactly those three'
$flagStage = Get-Content -Raw (Join-Path $flagBase 'plugin-free/staging.json') | ConvertFrom-Json
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $flagStage.proxy.path).Hash -ne $flagStage.proxy.sha256) {
    throw 'Openplanet injection proxy not restored exactly'
}
$flagResult = [pscustomobject]@{
    at=(Get-Date -Format o)
    gameStopped=$true
    events=$flagEvents
    installerSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $flagInstaller).Hash
    finalCustomFilesPresent=$false
    openplanetProxyRestored=$true
}
$flagResult | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $flagBase 'download/installer-validation.json')
$flagResult | ConvertTo-Json -Depth 5
