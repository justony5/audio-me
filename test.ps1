$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /out:bin\ConnectionTests.exe /reference:bin\AudioSwitch.exe ConnectionTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & .\bin\ConnectionTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /out:bin\StateTests.exe /reference:bin\AudioSwitch.exe StateTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'State test compilation failed.' }
    & .\bin\StateTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'State tests failed.' }
} finally { Pop-Location }
