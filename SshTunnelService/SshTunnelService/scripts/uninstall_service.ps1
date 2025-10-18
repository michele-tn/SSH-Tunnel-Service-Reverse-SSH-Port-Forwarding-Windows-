param([string]$serviceName = "SshTunnelService")

sc.exe stop $serviceName
sc.exe delete $serviceName
Write-Host "Service removed."
