$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path bin -Force | Out-Null
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:bin\AudioMe.exe /win32manifest:app.manifest /win32icon:Assets\AudioMe.ico /resource:Assets\AudioMe.ico,AudioMe.ico /resource:Theme.xaml,Theme.xaml /lib:$wpf /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll Audio.cs Program.cs Models.cs Widget.cs Flyout.cs Theme.cs UiElements.cs Branding.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -LiteralPath bin\AudioMe.exe -Destination bin\AudioSwitch.exe -Force
    Write-Output (Join-Path $PSScriptRoot 'bin\AudioMe.exe')
} finally { Pop-Location }
