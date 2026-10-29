# Pin and verify the bundled English Whisper base Q5_1 model.
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot 'Murmur\Models'
$model = Join-Path $dir 'speech-model.bin'
$expected = '4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f'
New-Item -ItemType Directory -Force $dir | Out-Null
if (-not (Test-Path -LiteralPath $model)) {
    $partial = Join-Path $dir 'speech-model.download'
    curl.exe --fail --location --show-error --connect-timeout 15 --max-time 300 --output $partial 'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.en-q5_1.bin?download=true'
    if ($LASTEXITCODE -ne 0) { throw 'Model download failed. Obtain the file through an approved download process.' }
    if ((Get-FileHash -LiteralPath $partial).Hash -ne $expected) { throw 'Downloaded model checksum mismatch' }
    Move-Item -LiteralPath $partial -Destination $model
}
if ((Get-FileHash -LiteralPath $model).Hash -ne $expected) { throw 'Existing model checksum mismatch' }
Write-Host "Verified model: $model"
