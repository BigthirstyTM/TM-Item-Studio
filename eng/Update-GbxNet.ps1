[CmdletBinding()]
param(
    [string] $Version,
    [string] $Repository = 'https://github.com/BigBang1112/gbx-net.git'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$patchPath = Join-Path $repoRoot 'patches\gbx-net-null-class-id.patch'
$skinPatchPath = Join-Path $repoRoot 'patches\gbx-net-skin-weights.patch'
$deformationPatchPath = Join-Path $repoRoot 'patches\gbx-net-crystal-deformation-layer.patch'
$outputPath = Join-Path $repoRoot 'lib\GBX.NET.dll'
$manifestPath = Join-Path $repoRoot 'lib\GBX.NET.version.json'

if (-not (Test-Path $patchPath)) {
    throw "Compatibility patch not found: $patchPath"
}
if (-not (Test-Path $deformationPatchPath)) {
    throw "Mesh Modeler deformation compatibility patch not found: $deformationPatchPath"
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
    # GBX.NET 2.5.0 contains generated source paths longer than the default
    # Windows Git checkout limit. Enable Git's long-path handling for this
    # clone only; this avoids requiring a machine-wide Git configuration.
    & git -c core.longpaths=true clone --quiet --depth 1 --branch $tag $Repository $sourcePath
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE." }

    $commit = (& git -C $sourcePath rev-parse HEAD).Trim()
    if ([string]::IsNullOrWhiteSpace($commit)) { throw 'Could not resolve the upstream commit.' }

    $patchHash = (Get-FileHash $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Test-Path $manifestPath) -and (Test-Path $outputPath)) {
        try {
            $existing = Get-Content -Raw $manifestPath | ConvertFrom-Json
            if ($existing.skinWeightsPatchSha256 -eq (Get-FileHash $skinPatchPath -Algorithm SHA256).Hash.ToLowerInvariant() -and
                $existing.deformationLayerPatchSha256 -eq (Get-FileHash $deformationPatchPath -Algorithm SHA256).Hash.ToLowerInvariant() -and $existing.version -eq $Version -and
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
    if ($LASTEXITCODE -eq 0) {
        & git -C $sourcePath apply $patchPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply the GBX.NET compatibility patch.' }
    }
    else {
        # GBX.NET 2.5.0 fixed the null class-id handling upstream. Accept that
        # equivalent implementation while still failing closed if the behavior
        # is absent or changed in a future release.
        $readerSource = (& git -C $sourcePath show "HEAD:Src/GBX.NET/Serialization/GbxReader.NodeRef.cs") -join "`n"
        if ($readerSource -match 'if \(encapsulation is not null && rawClassId == uint\.MaxValue\)') {
            $readerSource = $readerSource.Replace('if (encapsulation is not null && rawClassId == uint.MaxValue)', 'if (rawClassId == uint.MaxValue)')
            $readerFile = Join-Path $sourcePath 'Src\GBX.NET\Serialization\GbxReader.NodeRef.cs'
            Set-Content -LiteralPath $readerFile -Value $readerSource -Encoding UTF8
            Write-Host 'Applied null class-id compatibility to the GBX.NET 2.5 NodeRef reader.'
        }
        elseif ($readerSource -notmatch 'if \(rawClassId == uint\.MaxValue\)') {
            throw "The compatibility patch no longer applies to GBX.NET $Version and the upstream null-reference behavior was not found. Review the upstream change before updating."
        }
        else {
            Write-Host 'Upstream already contains the null class-id compatibility behavior; no local patch applied.'
        }
    }
    & git -C $sourcePath apply --check $skinPatchPath
    if ($LASTEXITCODE -eq 0) {
        & git -C $sourcePath apply $skinPatchPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply the GBX.NET skin weights patch.' }
    }
    else {
        # The generated vertex-stream source changed shape in 2.5.0. Apply the
        # same narrow Float3 blend-weight preservation contract by semantic
        # anchors when the historical patch context is no longer present.
        $vertexPath = Join-Path $sourcePath 'Src\GBX.NET\Engines\Plug\CPlugVertexStream.cs'
        $vertexSource = Get-Content -Raw $vertexPath
        $fieldAnchor = '    private SortedDictionary<int, int[]> colors = [];'
        $readAnchor = '                SetVec3(decl.Semantic, values);'
        if ($vertexSource.Contains('private Vec3[]? blendWeight3;')) {
            Write-Host 'Upstream vertex stream already contains the Float3 blend-weight compatibility field.'
        }
        elseif ($vertexSource.Contains($fieldAnchor) -and $vertexSource.Contains($readAnchor)) {
            $vertexSource = $vertexSource.Replace($fieldAnchor, $fieldAnchor + "`r`n    private Vec3[]? blendWeight3;")
            $vertexSource = $vertexSource.Replace($readAnchor, '                if (decl.Semantic == EPlugVDcl.BlendWeight) blendWeight3 = values;' + "`r`n" + $readAnchor)
            Set-Content -LiteralPath $vertexPath -Value $vertexSource -Encoding UTF8
            Write-Host 'Applied the Float3 blend-weight compatibility contract to the generated 2.5.0 vertex stream.'
        }
        else {
            throw "The skin weights patch no longer applies to GBX.NET $Version and its generated vertex-stream anchors were not found. Review the upstream change before updating."
        }
    }
    & git -C $sourcePath apply --check $deformationPatchPath
    if ($LASTEXITCODE -eq 0) {
        & git -C $sourcePath apply $deformationPatchPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply the Mesh Modeler deformation-layer compatibility patch.' }
    }
    else {
        $crystalSource = (& git -C $sourcePath show 'HEAD:Src/GBX.NET/Engines/Plug/CPlugCrystal.cs') -join "`n"
        $crystalLayout = (& git -C $sourcePath show 'HEAD:Src/GBX.NET/Engines/Plug/CPlugCrystal.chunkl') -join "`n"
        if ($crystalSource -notmatch 'ELayerType\.Deformation => new DeformationLayer\(\)' -or
            $crystalLayout -notmatch 'iso4\[\] Transforms') {
            throw 'The Mesh Modeler deformation-layer patch no longer applies and the upstream deformation layout was not found. Review the upstream change before updating.'
        }
        Write-Host 'Upstream already contains the Mesh Modeler deformation-layer compatibility behavior.'
    }

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
        deformationLayerPatchSha256 = (Get-FileHash $deformationPatchPath -Algorithm SHA256).Hash.ToLowerInvariant()
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
