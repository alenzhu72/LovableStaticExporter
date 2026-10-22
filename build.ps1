$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /out:LovableStaticExporter.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll App.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
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
    $packageFiles = @('LovableStaticExporter.exe','export.mjs','news.mjs','README.md') + @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.txt' | ForEach-Object { $_.FullName })
    Compress-Archive -LiteralPath $packageFiles -DestinationPath 'LovableStaticExporter.zip' -Force
} finally { Pop-Location }
