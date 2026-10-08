# Builds Extra Dim with the C# compiler that ships with Windows (.NET Framework 4),
# then puts an "Extra Dim" shortcut on the desktop. Nothing to install.
$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$csc   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $root 'build'
$dist  = Join-Path $root 'dist'
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $build, $dist, $assets | Out-Null

# Stop a running copy so the exe can be replaced
Get-Process ExtraDim -ErrorAction SilentlyContinue | Stop-Process -Force

& $csc /nologo /target:exe /out:"$build\MakeIcon.exe" /r:System.Drawing.dll "$root\tools\MakeIcon.cs" "$root\src\Art.cs"
if ($LASTEXITCODE) { throw 'MakeIcon failed to compile' }
& "$build\MakeIcon.exe" $assets
if ($LASTEXITCODE) { throw 'MakeIcon failed' }

# Signing key lives outside the project so it never gets uploaded with the code.
$privateKey = Join-Path $env:USERPROFILE '.extra-dim-signing\private-key.xml'
& $csc /nologo /target:exe /out:"$build\Sign.exe" "$root\tools\Sign.cs"
if ($LASTEXITCODE) { throw 'Sign failed to compile' }
& "$build\Sign.exe" key $privateKey "$build\publickey.xml"
if ($LASTEXITCODE) { throw 'Could not prepare the signing key' }

# Official download link shown by the tamper warning (edit download-link.txt to change it)
$linkFile = Join-Path $root 'download-link.txt'
$link = if (Test-Path $linkFile) { (Get-Content $linkFile -Raw).Trim() } else { '' }
if ($link -notmatch '^https://') { $link = 'https://github.com/LawalGoodness' }
"namespace ExtraDim { static class BuildInfo { public const string DownloadUrl = `"$($link.Replace('"',''))`"; public const string RepoUrl = `"https://github.com/LawalGoodness/extra-dim`"; } }" |
    Set-Content "$build\BuildInfo.cs" -Encoding ascii

& $csc /nologo /target:winexe /optimize+ /out:"$dist\ExtraDim.exe" `
    /win32icon:"$assets\ExtraDim.ico" /resource:"$assets\ExtraDim.ico,ExtraDim.icon.ico" `
    /resource:"$build\publickey.xml,ExtraDim.publickey.xml" `
    /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$root\src\ExtraDim.cs" "$root\src\Art.cs" "$root\src\Theme.cs" "$root\src\Integrity.cs" "$build\BuildInfo.cs"
if ($LASTEXITCODE) { throw 'ExtraDim failed to compile' }

& "$build\Sign.exe" sign $privateKey "$dist\ExtraDim.exe"
if ($LASTEXITCODE) { throw 'Signing failed' }

$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $desktop 'Extra Dim.lnk'))
$lnk.TargetPath = "$dist\ExtraDim.exe"
$lnk.WorkingDirectory = $dist
$lnk.IconLocation = "$dist\ExtraDim.exe,0"
$lnk.Description = 'Adjustable extra screen dimmer'
$lnk.Save()

Write-Host "Built $dist\ExtraDim.exe and added 'Extra Dim' to the desktop."
