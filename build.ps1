$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
Push-Location $PSScriptRoot
try {
    if (!(Test-Path -LiteralPath 'WinSCPnet.dll') -or !(Test-Path -LiteralPath 'WinSCP.exe')) { & .\prepare-deps.ps1 }
    & $compiler /nologo /target:winexe /out:LovableStaticExporter.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:WinSCPnet.dll App.cs Sftp.cs SftpPanel.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    & $compiler /nologo /target:winexe /out:server\SftpServerSetup.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll .\server\ServerSetup.cs
    if ($LASTEXITCODE -ne 0) { throw 'Server setup compilation failed.' }
    $serverErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'server\Install-SftpServer.ps1'),[ref]$null,[ref]$serverErrors) | Out-Null
    if ($serverErrors.Count -gt 0) { throw ($serverErrors | Out-String) }
    node --check export.mjs
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript validation failed.' }
    node --check news.mjs
    if ($LASTEXITCODE -ne 0) { throw 'News script validation failed.' }
    node --test tests/news.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'News tests failed.' }
    $testExe = Join-Path ([IO.Path]::GetTempPath()) ('lse-app-test-' + [guid]::NewGuid().ToString('N') + '.exe')
    try {
        & $compiler /nologo /target:exe "/out:$testExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll .\tests\AppTests.cs
        if ($LASTEXITCODE -ne 0) { throw 'Windows form tests compilation failed.' }
        & $testExe (Join-Path $PSScriptRoot 'LovableStaticExporter.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Windows form tests failed.' }
    } finally { Remove-Item -LiteralPath $testExe -Force -ErrorAction SilentlyContinue }
    $packageStage = Join-Path ([IO.Path]::GetTempPath()) ('lse-package-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $packageStage 'vendor') -Force | Out-Null
    try {
        $packageFiles = @('LovableStaticExporter.exe','export.mjs','news.mjs','README.md','WinSCPnet.dll','WinSCP.exe','server') + @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.txt' | ForEach-Object { $_.FullName })
        Copy-Item -LiteralPath $packageFiles -Destination $packageStage -Recurse
        Copy-Item -LiteralPath 'vendor\winscp' -Destination (Join-Path $packageStage 'vendor') -Recurse
        Compress-Archive -Path (Join-Path $packageStage '*') -DestinationPath 'LovableStaticExporter.zip' -Force
    } finally {
        $resolvedPackage = [IO.Path]::GetFullPath($packageStage)
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if ($resolvedPackage.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedPackage -Leaf).StartsWith('lse-package-')) { Remove-Item -LiteralPath $resolvedPackage -Recurse -Force }
    }
} finally { Pop-Location }
