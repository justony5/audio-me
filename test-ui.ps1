param([string]$Screenshots)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $wpf = Join-Path $framework 'WPF'
    & (Join-Path $framework 'csc.exe') /nologo /out:bin\UiChecks.exe /win32manifest:app.manifest /reference:bin\AudioSwitch.exe /lib:$wpf /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll UiChecks.cs
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    # -Screenshots <folder> captures README images with demo devices; it briefly shows windows on screen.
    if ($Screenshots) { & .\bin\UiChecks.exe --screenshots (Join-Path $PSScriptRoot $Screenshots) } else { & .\bin\UiChecks.exe }
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
} finally { Pop-Location }
