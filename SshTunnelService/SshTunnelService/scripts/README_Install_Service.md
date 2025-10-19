# SSH Tunnel Service Installation Script

This PowerShell script installs the **SshTunnelService** as a Windows Service running under the **LocalSystem** account, granting full administrative privileges.

---

## ⚙️ Purpose

The script automates the process of:
- Installing the service.
- Assigning SYSTEM privileges.
- Removing any previous installation.
- Starting the service automatically after installation.

---

## 📋 Usage

### 1. Open PowerShell as Administrator

Right-click PowerShell and choose **“Run as Administrator”**.  
Administrative privileges are required to install services.

### 2. Navigate to the scripts folder

```powershell
cd "C:\Path\To\SshTunnelService\scripts"
```

### 3. Execute the script

```powershell
.\install_service.ps1
```

Optionally specify custom parameters:

```powershell
.\install_service.ps1 -exePath "C:\Path\To\SshTunnelService.exe" -serviceName "MySshTunnelService"
```

---

## 🧩 Script Details

- Ensures PowerShell runs with **Administrator privileges**.
- Removes any existing service with the same name before reinstalling.
- Creates the Windows Service under the **LocalSystem** account.
- Sets automatic startup and service description.
- Starts the service immediately upon successful installation.

---

## 🧪 Verify Installation

To confirm that the service is installed and running as **LocalSystem**:

```powershell
Get-WmiObject Win32_Service -Filter "Name='SshTunnelService'" | Select Name, StartName
```

Expected output:

```
Name               StartName
----               ---------
SshTunnelService   LocalSystem
```

---

## 🧱 Troubleshooting

| Issue | Cause | Solution |
|-------|--------|-----------|
| `Access denied` | PowerShell not running as Administrator | Re-run PowerShell with “Run as Administrator” |
| `Executable not found` | Wrong path provided in `-exePath` | Ensure the path points to the compiled `.exe` file |
| `Service already exists` | Old service instance | The script automatically removes old instances |

---

## 📜 License

MIT License © 2025
