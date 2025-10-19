<#
.SYNOPSIS
Installs the SSH Tunnel Windows Service with SYSTEM privileges.

.DESCRIPTION
This script installs the SshTunnelService as a Windows Service,
running under the LocalSystem account with full administrative privileges.
#>

param(
    [string]$exePath = "$(Resolve-Path ..\bin\Debug\SshTunnelService.exe)",
    [string]$serviceName = "SshTunnelService"
)

# --- Ensure administrative privileges ---
If (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "[!] Restarting script as Administrator..."
    Start-Process powershell "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    Exit
}

# --- Verify the executable exists ---
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at $exePath"
    exit 1
}

Write-Host "Installing service '$serviceName' from path:"
Write-Host "  $exePath"
Write-Host ""

# --- Remove previous instance if it exists ---
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "[*] Existing service found. Removing..."
    sc.exe stop $serviceName | Out-Null
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- Create the service under LocalSystem ---
$cmd = "sc.exe create $serviceName binPath= `"$exePath`" start= auto obj= `".\LocalSystem`" DisplayName= `"SSH Tunnel Service`""
Write-Host "[*] Executing: $cmd"
Invoke-Expression $cmd

# --- Set description ---
sc.exe description $serviceName "SSH Tunnel Service (reverse SSH port forwarding)"

# --- Start the service ---
Write-Host "[*] Starting service..."
sc.exe start $serviceName

Write-Host ""
Write-Host "[✔] Service '$serviceName' installed and started successfully under LocalSystem."
Write-Host "[i] Verify with: Get-WmiObject Win32_Service -Filter `"Name='$serviceName'`" | Select Name, StartName"
