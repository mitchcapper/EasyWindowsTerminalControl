Set-StrictMode -version latest;
$ErrorActionPreference = "Stop";
$VerbosePreference="Continue";


$publicKey=(curl https://github.com/$($env:GITHUB_REPOSITORY_OWNER).keys) + " gh"
Function InstallSshServer(){
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [String] $publicKey
    )
    Add-WindowsCapability -Online -Name OpenSSH.Server
    echo "PubkeyAuthentication yes`nPasswordAuthentication no`nSubsystem sftp sftp-server.exe`nMatch Group administrators`n`tAuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys`n" | out-file -Encoding ASCII $env:programData/ssh/sshd_config
    ssh-keygen -A
    echo "$publicKey`n" | out-file -Encoding ASCII $env:programData/ssh/administrators_authorized_keys
    icacls.exe "$env:programData\ssh\administrators_authorized_keys" /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F"
    cat $env:programData/ssh/administrators_authorized_keys
}
Function DownloadStartCloudflareServer(){
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [String] $LocalHostnameAndPort, #ie 127.0.0.1:22
        [Parameter(Mandatory=$false)]
        [String] $SaveToFilename="cloudflared.exe" #can include path
    )
    if (([System.IO.File]::Exists($SaveToFilename)) -eq $false) {
        Invoke-WebRequest -URI https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe -OutFile $SaveToFilename
    }
    $myargs = "tunnel --no-autoupdate --url tcp://$LocalHostnameAndPort --logfile cfd.log"
    #$scriptBlock = [Scriptblock]::Create("Start-Process -NoNewWindow -Wait `"$SaveToFilename`" $myargs ")
    $myjob = Start-Process -PassThru -NoNewWindow `"$SaveToFilename`" -ArgumentList $myargs

    #Start-Job -Name CFD -ScriptBlock $scriptBlock
    #$myjob= Receive-Job -Name CFD
    return $myjob
}
Function InstallSSHStartCF(){
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [String] $publicKey,
        [Parameter(Mandatory=$false)]
        [String] $SaveToFilename="cloudflared.exe" #can include path
    )
    InstallSshServer $publicKey
    $server = DownloadStartCloudflareServer("127.0.0.1:22")
    $scriptBlock = [Scriptblock]::Create("Start-Process -NoNewWindow -Wait `"sshd.exe`" ")

    Start-Job -Name SSHD -ScriptBlock $scriptBlock
    return $server
}
InstallSSHStartCF $publicKey
while ($true) {
    Start-Sleep -Seconds 30
    cat cfd.log
}
#Wait-Job SSHD
