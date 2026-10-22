param(
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [switch]$MachineStore,
    [string]$SignToolPath,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$thumb = $CertificateThumbprint.Replace(' ','').ToUpperInvariant()
if ($thumb -notmatch '^[0-9A-F]{40}$') { throw 'Supply a real code-signing certificate thumbprint from the Windows certificate store.' }
$store = if ($MachineStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
$certificate = Get-Item -LiteralPath (Join-Path $store $thumb)
if (!$certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'Certificate must have an accessible private key and be within its validity period.' }
$eku = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
if (!$eku -or !@($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count) { throw 'The certificate is not a code-signing certificate.' }
$chain = New-Object Security.Cryptography.X509Certificates.X509Chain
try { if (!$chain.Build($certificate)) { throw 'Certificate must chain to a trusted root; no root certificates are installed by this script.' } } finally { $chain.Dispose() }
if (!$SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $tool = Get-ChildItem -Path (Join-Path $kitRoot '*\x64\signtool.exe') -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { $SignToolPath = $tool.FullName }
    }
}
if (!$SignToolPath -or !(Test-Path -LiteralPath $SignToolPath)) { throw 'Install Windows SDK Signing Tools, or supply -SignToolPath.' }
if ($TimestampServer -notmatch '^https?://') { throw 'Provide a valid trusted timestamp service URL.' }
Push-Location $PSScriptRoot
try {
    & .\build.ps1
    $targets = @('LovableStaticExporter.exe','server\SftpServerSetup.exe')
    foreach ($target in $targets) {
        $arguments = @('sign','/sha1',$thumb,'/s','My','/fd','SHA256','/tr',$TimestampServer,'/td','SHA256','/d','Lovable Static Exporter','/du','https://github.com/alenzhu72/LovableStaticExporter')
        if ($MachineStore) { $arguments += '/sm' }
        & $SignToolPath @arguments $target
        if ($LASTEXITCODE -ne 0) { throw ('Signing failed: ' + $target) }
        $signature = Get-AuthenticodeSignature -LiteralPath $target
        if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) { throw ('Signature/timestamp validation failed: ' + $target) }
    }
    $script = 'server\Install-SftpServer.ps1'
    $signedScript = Set-AuthenticodeSignature -LiteralPath $script -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    if ($signedScript.Status -ne 'Valid' -or !(Get-AuthenticodeSignature -LiteralPath $script).TimeStamperCertificate) { throw 'Server script signature/timestamp validation failed.' }
    # Package without recompiling; rebuilding after signing would invalidate signatures.
    & .\build.ps1 -PackageOnly
    Write-Host 'Signed, timestamped, verified and repackaged. SmartScreen reputation may still need to build.'
} finally { Pop-Location }
