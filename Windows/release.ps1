# Build and sign a release with Azure Artifact Signing. Requires an authenticated Azure CLI profile.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SigningMetadataPath,
    [Parameter(Mandatory)][version]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$OutputName,
    [ValidateSet('Ggml', 'Microsoft')][string]$Edition = 'Ggml',
    [string]$AzureConfigDir = (Join-Path $env:USERPROFILE '.config\azure-personal'),
    [string]$SigningClientVersion = '1.0.146'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$metadata = (Resolve-Path -LiteralPath $SigningMetadataPath).Path
$dist = Join-Path $root 'dist\windows'
$layout = Join-Path $dist ('release-layout-' + [guid]::NewGuid().ToString('N'))
$toolsDir = Join-Path $root "dist\artifact-signing-$SigningClientVersion"
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
New-Item -ItemType Directory -Force $dist | Out-Null
dotnet publish (Join-Path $PSScriptRoot 'Murmur\Murmur.csproj') -c Release -r win-x64 -p:SpeechEdition=$Edition -o $layout
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item (Join-Path $PSScriptRoot 'Murmur\Package.appxmanifest') (Join-Path $layout 'AppxManifest.xml')
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Murmur\Assets\*.png') (Join-Path $layout 'Assets')
$sdk = Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$signTool = Get-ChildItem $sdk.FullName -Recurse -Filter signtool.exe | Where-Object FullName -like '*\x64\*' | Select-Object -First 1
$makeAppx = Get-ChildItem $sdk.FullName -Recurse -Filter makeappx.exe | Where-Object FullName -like '*\x64\*' | Select-Object -First 1
if (-not $signTool -or -not $makeAppx) { throw 'Windows SDK signing tools were not restored' }
$dlib = Join-Path $toolsDir 'bin\x64\Azure.CodeSigning.Dlib.dll'
if (-not (Test-Path -LiteralPath $dlib)) {
    New-Item -ItemType Directory -Force $toolsDir | Out-Null
    $zip = Join-Path $toolsDir 'client.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.artifactsigning.client/$SigningClientVersion/microsoft.artifactsigning.client.$SigningClientVersion.nupkg" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $toolsDir -Force
}
$previousConfig = $env:AZURE_CONFIG_DIR
try {
    $env:AZURE_CONFIG_DIR = $AzureConfigDir
    $exe = Join-Path $layout 'Murmur.exe'
    $dll = Join-Path $layout 'Murmur.dll'
    & $signTool.FullName sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $dlib /dmdf $metadata $exe $dll
    if ($LASTEXITCODE -ne 0) { throw 'Application signing failed' }
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    [xml]$manifest = Get-Content -LiteralPath $manifestPath
    $manifest.Package.Identity.Publisher = (Get-AuthenticodeSignature -LiteralPath $exe).SignerCertificate.Subject
    $manifest.Package.Identity.Version = $Version.ToString(4)
    $msix = Join-Path $dist "$OutputName.msix"
    $manifest.Save($manifestPath)
    & $makeAppx.FullName pack /o /d $layout /p $msix *> (Join-Path $dist "$OutputName-pack.log")
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packing failed; see pack log' }
    & $signTool.FullName sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $dlib /dmdf $metadata $msix
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed' }
    & $signTool.FullName verify /pa /all $msix $exe $dll
    if ($LASTEXITCODE -ne 0) { throw 'Release signature verification failed' }
    $dep = $manifest.Package.Dependencies.PackageDependency
    if ($dep) {
        $runtime = Get-ChildItem (Join-Path $nuget 'microsoft.windowsappsdk.runtime') -Recurse -Filter "$($dep.Name).msix" |
            Where-Object FullName -like '*\win10-x64\*' | Select-Object -First 1
        if (-not $runtime) { throw 'Required Windows App Runtime package not found' }
        Copy-Item -LiteralPath $runtime.FullName -Destination $dist -Force
    }
    Write-Host "Verified release: $msix"
}
finally { $env:AZURE_CONFIG_DIR = $previousConfig }
