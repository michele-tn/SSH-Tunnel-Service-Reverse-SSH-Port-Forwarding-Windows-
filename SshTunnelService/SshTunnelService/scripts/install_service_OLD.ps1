param(
    [string]$exePath = "$(Resolve-Path ..\bin\Debug\SshTunnelService.exe)",
    [string]$serviceName = "SshTunnelService"
)

if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at $exePath"
    exit 1
}

sc.exe create $serviceName binPath= "`"$exePath`"" start= auto DisplayName= "SSH Tunnel Service"
sc.exe description $serviceName "SSH Tunnel Service (reverse SSH port forwarding)"
sc.exe start $serviceName
