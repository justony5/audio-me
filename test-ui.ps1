$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /out:bin\UiChecks.exe /reference:bin\AudioSwitch.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll UiChecks.cs
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    & .\bin\UiChecks.exe
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
} finally { Pop-Location }
