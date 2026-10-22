#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$OpenSSHZip,
    [Parameter(Mandatory=$true)][string]$PublishPath,
    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9_-]{0,30}$')][string]$UserName = 'lse_deploy',
    [ValidateRange(1,65535)][int]$Port = 22,
    [Parameter(Mandatory=$true)][string]$AllowedClientAddress
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$target = [IO.Path]::GetFullPath($PublishPath).TrimEnd('\')
if ($target -notmatch '^[A-Za-z]:\\.+' -or $target -match '["\r\n]') { throw 'Use a full local website path, not a drive root.' }
if ($AllowedClientAddress -match '[\r\n]' -or [string]::IsNullOrWhiteSpace($AllowedClientAddress)) { throw 'Specify your client public IP/CIDR, or explicitly use Any.' }
$service = Get-Service -Name sshd -ErrorAction SilentlyContinue
$sshdExe = $null
if ($service) {
    $serviceCommand = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\sshd').ImagePath
    if ($serviceCommand -match '^"([^"]+sshd\.exe)"') { $sshdExe = $Matches[1] }
    elseif ($serviceCommand -match '^(.+?sshd\.exe)(?:\s|$)') { $sshdExe = $Matches[1] }
    if (!$sshdExe -or !(Test-Path -LiteralPath $sshdExe)) { throw 'Cannot locate the existing sshd executable. Configure this installation manually.' }
} else {
    if (!(Test-Path -LiteralPath $OpenSSHZip -PathType Leaf)) { throw 'Download the official OpenSSH-Win64.zip release and select it first.' }
    $installDir = Join-Path $env:ProgramFiles 'OpenSSH'
    if ((Test-Path -LiteralPath $installDir) -and @(Get-ChildItem -LiteralPath $installDir -Force).Count -gt 0) { throw 'OpenSSH installation directory is not empty. Review it before installing.' }
    $extract = Join-Path ([IO.Path]::GetTempPath()) ('lse-openssh-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $extract | Out-Null
    try {
        Expand-Archive -LiteralPath $OpenSSHZip -DestinationPath $extract
        $installer = @(Get-ChildItem -LiteralPath $extract -Filter 'install-sshd.ps1' -Recurse)
        if ($installer.Count -ne 1) { throw 'This ZIP is not a supported OpenSSH-Win64 archive.' }
        $binary = Join-Path $installer[0].DirectoryName 'sshd.exe'
        if ((Get-AuthenticodeSignature -LiteralPath $binary).Status -ne 'Valid') { throw 'sshd.exe must have a valid publisher signature. Use an official signed release.' }
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        Copy-Item -Path (Join-Path $installer[0].DirectoryName '*') -Destination $installDir -Recurse -Force
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installDir 'install-sshd.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'OpenSSH service installation failed.' }
        $sshdExe = Join-Path $installDir 'sshd.exe'
    } finally {
        $resolvedExtract = [IO.Path]::GetFullPath($extract)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if ($resolvedExtract.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedExtract -Leaf).StartsWith('lse-openssh-')) { Remove-Item -LiteralPath $resolvedExtract -Recurse -Force }
    }
}
$binaryDir = Split-Path $sshdExe -Parent
$sshData = Join-Path $env:ProgramData 'ssh'
New-Item -ItemType Directory -Path $sshData -Force | Out-Null
$configPath = Join-Path $sshData 'sshd_config'
if (!(Test-Path -LiteralPath $configPath)) {
    $defaultConfig = Join-Path $binaryDir 'sshd_config_default'
    if (!(Test-Path -LiteralPath $defaultConfig)) { throw 'Cannot find sshd_config_default.' }
    Copy-Item -LiteralPath $defaultConfig -Destination $configPath
}
$config = [IO.File]::ReadAllText($configPath)
$begin = '# LSE-SFTP BEGIN ' + $UserName
$end = '# LSE-SFTP END ' + $UserName
$config = [regex]::Replace($config, '(?ms)^' + [regex]::Escape($begin) + '\r?\n.*?^' + [regex]::Escape($end) + '\r?\n?', '')
$globalConfig = [regex]::Split($config,'(?im)^\s*Match\s+',2)[0]
$activePorts = @([regex]::Matches($globalConfig,'(?im)^\s*Port\s+(\d+)\s*(?:#.*)?$') | ForEach-Object { [int]$_.Groups[1].Value })
if ($activePorts.Count -gt 0 -and ($activePorts.Count -ne 1 -or $activePorts[0] -ne $Port)) { throw 'Existing SSH port differs. Use the existing port or review sshd_config manually.' }
if ($activePorts.Count -eq 0) { $config = 'Port ' + $Port + "`r`n" + $config }
if ($config -match ('(?im)^\s*Match\s+User\s+.*\b' + [regex]::Escape($UserName) + '(?:\s|$)')) { throw 'An existing Match User rule needs manual review for this account.' }
if (!(Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue)) {
    $password = Read-Host ('Set a strong password for new SFTP user ' + $UserName) -AsSecureString
    New-LocalUser -Name $UserName -Password $password -Description 'Lovable static website SFTP deployment' | Out-Null
    $usersGroup = Get-LocalGroup -SID 'S-1-5-32-545'
    Add-LocalGroupMember -Group $usersGroup.Name -Member $UserName -ErrorAction SilentlyContinue
}
$account = Get-LocalUser -Name $UserName
$administrators = Get-LocalGroup -SID 'S-1-5-32-544'
if (@(Get-LocalGroupMember -Group $administrators.Name | Where-Object { $_.SID -eq $account.SID }).Count -gt 0) { throw 'Use a dedicated non-administrator SFTP account.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
$acl = Get-Acl -LiteralPath $target
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($account.SID,'Modify','ContainerInherit,ObjectInherit','None','Allow')
$acl.SetAccessRule($rule)
try {
    $iisSid = (New-Object System.Security.Principal.NTAccount('IIS_IUSRS')).Translate([System.Security.Principal.SecurityIdentifier])
    $readRule = New-Object System.Security.AccessControl.FileSystemAccessRule($iisSid,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($readRule)
} catch { Write-Host 'IIS_IUSRS was not found. Install IIS and grant website read permission separately.' }
Set-Acl -LiteralPath $target -AclObject $acl
$sftpStart = $target.Replace('\','/')
$block = "`r`n$begin`r`nMatch User $UserName`r`n    ForceCommand internal-sftp -d `"$sftpStart`"`r`n    AllowTcpForwarding no`r`n    X11Forwarding no`r`n    PermitTunnel no`r`n    PasswordAuthentication yes`r`n$end`r`n"
$backup = $configPath + '.lse-backup-' + (Get-Date -Format 'yyyyMMddHHmmssfff')
Copy-Item -LiteralPath $configPath -Destination $backup
[IO.File]::WriteAllText($configPath,$config + $block,(New-Object Text.UTF8Encoding($false)))
try {
    & (Join-Path $binaryDir 'ssh-keygen.exe') -A
    if ($LASTEXITCODE -ne 0) { throw 'Host key generation failed.' }
    & $sshdExe -t -f $configPath
    if ($LASTEXITCODE -ne 0) { throw 'sshd configuration validation failed.' }
    $ruleName = 'LovableStaticExporter-SFTP-' + $Port
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -Name $ruleName }
    New-NetFirewallRule -Name $ruleName -DisplayName ('Lovable SFTP ' + $Port) -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress $AllowedClientAddress | Out-Null
    Set-Service sshd -StartupType Automatic
    Restart-Service sshd
} catch { Copy-Item -LiteralPath $backup -Destination $configPath -Force; throw }
Write-Host "`nSFTP setup complete. Fill these values in the desktop app:"
Write-Host ('User: ' + $UserName)
Write-Host ('Port: ' + $Port)
Write-Host ('Remote folder: /' + $sftpStart)
Write-Host 'Server SHA-256 host key fingerprints (compare with the desktop app):'
Get-ChildItem -LiteralPath $sshData -Filter 'ssh_host_*_key.pub' | ForEach-Object { & (Join-Path $binaryDir 'ssh-keygen.exe') -lf $_.FullName -E sha256 }
Write-Host ('Configuration backup: ' + $backup)
Write-Host 'Also allow this TCP port in your cloud firewall/security group. Other existing firewall rules are unchanged.'
Write-Host 'This account is SFTP-only; NTFS permissions still govern access to other readable server folders.'
