$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & .\build.ps1
    & .\test.ps1
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'bin\AudioMe.exe')).FileVersion
    $releaseName = "AudioMe-$version-win-x64"
    $destination = Join-Path $PSScriptRoot "dist\$releaseName"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath bin\AudioMe.exe -Destination $destination -Force
    Copy-Item -LiteralPath RELEASE-README.txt -Destination $destination -Force
    Compress-Archive -LiteralPath (Join-Path $destination 'AudioMe.exe'),(Join-Path $destination 'RELEASE-README.txt') -DestinationPath "dist\$releaseName.zip" -Force
    $hash = Get-FileHash -LiteralPath "dist\$releaseName.zip" -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $releaseName.zip" | Set-Content -LiteralPath "dist\$releaseName.zip.sha256" -Encoding ascii
    Write-Output "Release: $PSScriptRoot\dist\$releaseName.zip"
} finally { Pop-Location }
