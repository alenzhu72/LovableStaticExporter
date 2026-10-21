$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /out:LovableStaticExporter.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll App.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    node --check export.mjs
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript validation failed.' }
    Compress-Archive -LiteralPath 'LovableStaticExporter.exe','export.mjs','使用说明.txt' -DestinationPath 'LovableStaticExporter.zip' -Force
} finally { Pop-Location }
