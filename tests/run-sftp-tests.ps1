param([string]$PythonExe = 'python', [string]$ParamikoPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('lse-sftp-test-' + [guid]::NewGuid().ToString('N'))
$process = $null
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'WinSCP.exe'),(Join-Path $repoRoot 'WinSCPnet.dll') -Destination $fixtureRoot
    $testExe = Join-Path $fixtureRoot 'SftpTests.exe'
    & $compiler /nologo /target:exe "/out:$testExe" /reference:System.Web.Extensions.dll /reference:System.Security.dll "/reference:$(Join-Path $repoRoot 'WinSCPnet.dll')" (Join-Path $repoRoot 'Sftp.cs') (Join-Path $PSScriptRoot 'SftpTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'SFTP test compilation failed.' }
    $fixtureScript = Join-Path $PSScriptRoot 'sftp-fixture.py'
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $PythonExe
    $start.Arguments = '"' + $fixtureScript + '" "' + $fixtureRoot + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    if ($ParamikoPath) { $start.EnvironmentVariables['PYTHONPATH'] = $ParamikoPath }
    $process = [Diagnostics.Process]::Start($start)
    $ready = Join-Path $fixtureRoot 'ready.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path -LiteralPath $ready)) {
        if ($process.HasExited) { throw 'SFTP fixture exited. Install Python paramiko first.' }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'SFTP fixture startup timed out.' }
        Start-Sleep -Milliseconds 100
    }
    & $testExe $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'SFTP integration tests failed.' }
} finally {
    if ($process -and !$process.HasExited) {
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'stop') -Value 'stop'
        if (!$process.WaitForExit(5000)) { $process.Kill() }
    }
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('lse-sftp-test-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
