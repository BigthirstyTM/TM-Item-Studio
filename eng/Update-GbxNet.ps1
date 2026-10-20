[CmdletBinding()]
param(
    [string] $Version,
    [string] $Repository = 'https://github.com/BigBang1112/gbx-net.git'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$patchPath = Join-Path $repoRoot 'patches\gbx-net-null-class-id.patch'
$skinPatchPath = Join-Path $repoRoot 'patches\gbx-net-skin-weights.patch'
$outputPath = Join-Path $repoRoot 'lib\GBX.NET.dll'
$manifestPath = Join-Path $repoRoot 'lib\GBX.NET.version.json'

if (-not (Test-Path $patchPath)) {
    throw "Compatibility patch not found: $patchPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $index = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/gbx.net/index.json'
    $stable = @($index.versions | Where-Object { $_ -notmatch '-' } | ForEach-Object {
        try { [version]$_ } catch { $null }
    } | Where-Object { $_ -ne $null } | Sort-Object -Descending)
    if ($stable.Count -eq 0) {
        throw 'NuGet did not return a stable GBX.NET version.'
    }
    $Version = $stable[0].ToString()
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be a stable semantic version (for example 2.4.5), got '$Version'."
}

$tag = "v$Version"
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("tmis-gbx-net-" + [guid]::NewGuid().ToString('N'))
$sourcePath = Join-Path $scratch 'gbx-net'

try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    Write-Host "Cloning GBX.NET $tag..."
    & git clone --quiet --depth 1 --branch $tag $Repository $sourcePath
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE." }

    $commit = (& git -C $sourcePath rev-parse HEAD).Trim()
    if ([string]::IsNullOrWhiteSpace($commit)) { throw 'Could not resolve the upstream commit.' }

    $patchHash = (Get-FileHash $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Test-Path $manifestPath) -and (Test-Path $outputPath)) {
        try {
            $existing = Get-Content -Raw $manifestPath | ConvertFrom-Json
            if ($existing.skinWeightsPatchSha256 -eq (Get-FileHash $skinPatchPath -Algorithm SHA256).Hash.ToLowerInvariant() -and $existing.version -eq $Version -and
                $existing.upstreamCommit -eq $commit -and
                $existing.compatibilityPatchSha256 -eq $patchHash) {
                Write-Host "GBX.NET $Version ($commit) is already current."
                return
            }
        }
        catch {
            Write-Verbose "Ignoring an unreadable existing manifest: $($_.Exception.Message)"
        }
    }

    & git -C $sourcePath apply --check $patchPath
    if ($LASTEXITCODE -ne 0) {
        throw "The compatibility patch no longer applies to GBX.NET $Version. Review the upstream change before updating."
    }
    & git -C $sourcePath apply $patchPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply the GBX.NET compatibility patch.' }
    & git -C $sourcePath apply $skinPatchPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply the GBX.NET skin weights patch.' }

    Write-Host 'Building the patched net8.0 assembly...'
    & dotnet build (Join-Path $sourcePath 'Src\GBX.NET\GBX.NET.csproj') -c Release -f net8.0 --nologo -m:2 -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    $builtPath = Join-Path $sourcePath 'Src\GBX.NET\bin\Release\net8.0\GBX.NET.dll'
    if (-not (Test-Path $builtPath)) { throw "Build output not found: $builtPath" }
    Copy-Item $builtPath $outputPath -Force

    $manifest = [ordered]@{
        package = 'GBX.NET'
        version = $Version
        upstreamRepository = 'https://github.com/BigBang1112/gbx-net'
        upstreamTag = $tag
        upstreamCommit = $commit
        targetFramework = 'net8.0'
        compatibilityPatch = 'patches/gbx-net-null-class-id.patch'
        compatibilityPatchSha256 = $patchHash
        skinWeightsPatchSha256 = (Get-FileHash $skinPatchPath -Algorithm SHA256).Hash.ToLowerInvariant()
        updateCommand = 'powershell -NoProfile -ExecutionPolicy Bypass -File eng/Update-GbxNet.ps1'
    }
    $manifest | ConvertTo-Json | Set-Content $manifestPath -Encoding UTF8

    Write-Host "Updated $outputPath to GBX.NET $Version ($commit)."
}
finally {
    if (Test-Path $scratch) {
        $resolvedScratch = (Resolve-Path -LiteralPath $scratch).Path
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $scratchParent = [IO.Path]::GetDirectoryName($resolvedScratch)
        if ($scratchParent -ne $resolvedTemp -or
            [IO.Path]::GetFileName($resolvedScratch) -notmatch '^tmis-gbx-net-[0-9a-f]{32}$') {
            throw "Refusing to remove an unexpected temporary path: $resolvedScratch"
        }
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
