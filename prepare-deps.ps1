$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$expected = '40F20147604AE24C3B164A38F867AFCF12C30C317885DCA38A851F434003C08C'
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ('lse-winscp-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir | Out-Null
try {
    $archive = Join-Path $tempDir 'winscp.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/winscp/6.7.0/winscp.6.7.0.nupkg' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'WinSCP package SHA-256 mismatch.' }
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $tempDir 'package')
    $exe = Join-Path $tempDir 'package\tools\WinSCP.exe'
    if ((Get-AuthenticodeSignature -LiteralPath $exe).Status -ne 'Valid') { throw 'WinSCP executable signature is not valid.' }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $PSScriptRoot 'WinSCP.exe') -Force
    Copy-Item -LiteralPath (Join-Path $tempDir 'package\lib\net40\WinSCPnet.dll') -Destination (Join-Path $PSScriptRoot 'WinSCPnet.dll') -Force
} finally {
    $resolved = [IO.Path]::GetFullPath($tempDir)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('lse-winscp-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
