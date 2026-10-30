# Records promo footage with demo devices into -Out (default %USERPROFILE%\AudioMe-promo\public\footage, outside OneDrive).
# Shows windows full screen for ~40 s; don't touch the mouse while it runs. Pass -Shot main|flyout|editor|accent to record one.
param([string]$Shot, [string]$Out = (Join-Path $env:USERPROFILE 'AudioMe-promo\public\footage'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $wpf = Join-Path $framework 'WPF'
    & (Join-Path $framework 'csc.exe') /nologo /out:bin\PromoCapture.exe /win32manifest:app.manifest /reference:bin\AudioSwitch.exe /lib:$wpf /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll promo\capture\Capture.cs
    if ($LASTEXITCODE -ne 0) { throw 'Capture compilation failed.' }
    if ($Shot) { & .\bin\PromoCapture.exe $Out $Shot } else { & .\bin\PromoCapture.exe $Out }
    if ($LASTEXITCODE -ne 0) { throw 'Recording failed.' }
} finally { Pop-Location }
