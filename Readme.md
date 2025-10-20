# SSH Tunnel Service — Reverse SSH Port Forwarding (Windows)

A Windows service implemented in C# that creates and maintains reverse SSH tunnels (remote port forwarding) from a reachable SSH server back to services running on a Windows host. The project is supplied as a Visual Studio solution and ready-to-run sources. It uses the Renci.SshNet library to manage SSH connections and SQLite for local logging.

This README describes the repository layout as found in the distributed package, full configuration details (app.config), build and deployment instructions, usage (console and service modes), scripts included with the project, dependencies, troubleshooting, security guidance and developer notes.

Table of contents
- Project overview
- What the repository contains (detailed structure)
- Requirements and dependencies
- Configuration (app.config explained)
- How the service works (high level)
- Build and run instructions
  - Build with Visual Studio
  - Run in console for testing
  - Install as Windows service (scripts)
  - Uninstall service
- Scripts included and how to use them
- Logging and database
- Files requiring attention (keys, native SQLite interop)
- Examples and equivalent SSH commands
- Troubleshooting
- Security best practices
- Development notes and where to look in source
- License

---

Project overview

The SshTunnelService project is intended to run as a Windows Service (or as a console application while testing) and to create persistent reverse SSH tunnels from a remote SSH server to local services. It monitors the SSH sessions and automatically reconnects if a session fails. The implementation relies on SSH.NET to perform SSH connections from managed code and uses a local SQLite-based logging mechanism for diagnostic events and history.

Primary goals:
- Provide unattended, persistent reverse port forwarding on Windows hosts.
- Keep tunnels alive across network interruptions and reboots.
- Log events locally for audit and troubleshooting.

<div style="text-align: center;">
  <a href="https://github.com/michele-tn/SSH-Tunnel-Service-Reverse-SSH-Port-Forwarding-Windows-/blob/342395dc35c0d83fe0702cfe0dc2b9bff485d65f/LOCAL_DB.jpg" target="_blank">
    <img src="https://github.com/michele-tn/SSH-Tunnel-Service-Reverse-SSH-Port-Forwarding-Windows-/raw/342395dc35c0d83fe0702cfe0dc2b9bff485d65f/LOCAL_DB.jpg"
         alt="LOCAL_DB"
         style="max-width: 100%; height: auto; display: block; margin: 0 auto;">
  </a>

  <br>

  <a href="https://github.com/michele-tn/SSH-Tunnel-Service-Reverse-SSH-Port-Forwarding-Windows-/blob/342395dc35c0d83fe0702cfe0dc2b9bff485d65f/INTERACTIVE_MODE.jpg" target="_blank">
    <img src="https://github.com/michele-tn/SSH-Tunnel-Service-Reverse-SSH-Port-Forwarding-Windows-/raw/342395dc35c0d83fe0702cfe0dc2b9bff485d65f/INTERACTIVE_MODE.jpg"
         alt="INTERACTIVE_MODE"
         style="max-width: 100%; height: auto; display: block; margin: 0 auto;">
  </a>
</div>



---

Repository layout (exact structure discovered)

Root (SshTunnelService)
- packages/                              <- NuGet packages folder produced by package restore
- SshTunnelService/                      <- Visual Studio project folder (contains code & assets)
- SshTunnelService.sln                   <- Visual Studio solution

SshTunnelService (project contents)
- App.config                             <- Primary runtime configuration used by the .NET app
- packages.config                        <- NuGet package references for older VS package management
- SshTunnelService.csproj                <- C# project file
- /Core/
  - /Models/
    - TunnelDefinition.cs                 <- Model class that represents one tunnel definition
  - /Services/
    - SshTunnelManager.cs                 <- Core manager that orchestrates SSH connections/tunnels
- /Database/                              <- Database-related code or artifacts (SQLite database may be created here at runtime)
- /Infrastructure/
  - DbLoggingService.cs                   <- Service implementing logging into SQLite / DB
- /Lib/
  - /SQLite/
    - System.Data.SQLite.dll
    - System.Data.SQLite.Linq.dll
    - /x64/SQLite.Interop.dll
    - /x86/SQLite.Interop.dll
  - /SSH.NET/
    - Renci.SshNet.dll
- /Properties/
  - AssemblyInfo.cs
- /Resources/
  - guest_private_key.pem                  <- example/private key shipped in the distribution (replace in production)
- /scripts/
  - install_service.ps1                    <- PowerShell script to install the service (sc.exe or helper)
  - test_tunnel.ps1                        <- PowerShell script to test tunnels locally
  - uninstall_service.ps1                  <- PowerShell script to remove the service
- /ServiceHost/
  - Program.cs                             <- Program entry; registers / runs the Windows service or console mode
  - TunnelService.cs                       <- ServiceBase implementation that hosts SshTunnelManager

Notes:
- The Lib folder contains third-party runtime assemblies: Renci.SshNet (managed) and System.Data.SQLite (managed + native interop). The native SQLite interop DLLs are present for both x86 and x64.
- A sample PEM key (Resources/guest_private_key.pem) is included in the distribution; it is for example/testing only and must be replaced or removed for production.

---

Requirements & dependencies

Runtime:
- .NET Framework compatible with the supported runtime. The included app.config indicates the supported runtime sku: .NETFramework,Version=v4.8.1. Ensure a .NET runtime supporting 4.8.1 is installed on the target machine.

Third-party libraries:
- Renci.SshNet (Renci.SshNet.dll) — used to create SSH sessions and request reverse port forwards.
- System.Data.SQLite (System.Data.SQLite.dll and SQLite.Interop.dll for x86/x64) — used by DbLoggingService to persist logs/events to a local SQLite database.

Development/build:
- Visual Studio (solution labeled as "ReadyToRun" for Visual Studio 2015 in the package). Use Visual Studio 2015 or a newer Visual Studio version; if using newer versions, restore NuGet packages and migrate project if prompted.

Platform specifics:
- The native SQLite interop DLLs are provided for both x86 and x64. Ensure the build and deployment choose the correct native library for the target process bitness.

---

Configuration — app.config (complete example)

The service reads its main configuration from App.config (XML). This file shipped with the package contains the following keys:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup>
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8.1"/>
  </startup>
  <appSettings>
    <add key="SshHost" value="YOUR_IP_SERVER"/>
    <add key="SshPort" value="YOUR_PORT_SSH_SERVER"/>
    <add key="SshUser" value="YOUR_SshUser"/>
    <add key="MaxTunnels" value="5"/>
    <add key="HeartbeatIntervalMs" value="30000"/>
    <!-- format: remoteHost:remotePort:localHost:localPort,remoteHost2:remotePort2:localHost2:localPort2 -->
    <add key="Tunnels" value="remoteHost:remotePort:localHost:localPort,remoteHost2:remotePort2:localHost2:localPort2"/>
  </appSettings>
</configuration>
```

Key descriptions and recommended values:
- SshHost: remote SSH server hostname or IP (e.g. ssh.example.com or 203.0.113.10).
- SshPort: remote SSH port (typically 22).
- SshUser: username used to authenticate on the SSH server.
- MaxTunnels: maximum number of tunnels the service manages concurrently (integer). Set according to your needs and server limits.
- HeartbeatIntervalMs: monitoring/heartbeat interval in milliseconds (default example 30000 = 30s). Controls how frequently the manager checks connection health.
- Tunnels: comma-separated list of tunnel definitions in the format remoteHost:remotePort:localHost:localPort.
  - Example: `127.0.0.1:2222:127.0.0.1:3389,0.0.0.0:8080:127.0.0.1:80`

Tunnels format details:
- remoteHost: address on the SSH server side to bind (127.0.0.1 for server-only, 0.0.0.0 to bind all interfaces — requires GatewayPorts yes on the SSH server).
- remotePort: port number to accept traffic on the server.
- localHost/localPort: target on the Windows machine to receive forwarded traffic.

Important: after editing App.config, rebuild the project or ensure the deployed executable's .config (MyExecutable.exe.config) is updated accordingly.

---

How the service works (high level)

- Startup
  - Program.cs (ServiceHost) starts the application. If running as a service it registers TunnelService (ServiceBase). In console mode it runs the same manager loop for diagnostic purposes.
- Configuration parsing
  - App.config is parsed on startup. TunnelDefinition objects are created (Core/Models/TunnelDefinition.cs) from the Tunnels string.
- Tunnel management
  - SshTunnelManager.cs (Core/Services) uses SSH.NET to create SSH sessions and request reverse port forwards for each TunnelDefinition.
  - The manager monitors sessions and reconnects on failure using heartbeat and backoff logic (implementation details are in SshTunnelManager.cs).
- Logging
  - DbLoggingService (Infrastructure) writes lifecycle events, errors, and reconnect attempts to a local SQLite database or to configured logging sinks.
- Service lifecycle
  - The Windows service lifecycle (start/stop) is implemented in ServiceHost/TunnelService.cs and delegates to the SshTunnelManager for graceful shutdown/restart.

---

Build & run instructions

1. Restore NuGet packages
   - Open `SshTunnelService.sln` with Visual Studio.
   - Restore NuGet packages (Visual Studio will prompt or use the menu: Tools -> NuGet Package Manager -> Package Manager Console: `Update-Package -reinstall` if needed).
   - The `packages` folder may already contain restored packages in the distribution.

2. Build
   - Recommended configuration: Release (or Debug for development).
   - Choose platform according to the intended bitness (AnyCPU/x86/x64). For SQLite native interop, ensure the process bitness matches the provided SQLite.Interop.dll (x86 vs x64).
   - Build the solution.

3. Deploy the executable and dependencies
   - Locate built executable under `SshTunnelService\bin\Release\` (or Debug). Copy the following to your target installation folder (e.g., `C:\Program Files\SshTunnelService\`):
     - `SshTunnelService.exe` (or the ServiceHost exe produced by the project)
     - `SshTunnelService.exe.config` (the generated .config based on App.config)
     - `Renci.SshNet.dll`
     - `System.Data.SQLite.dll` and the appropriate `SQLite.Interop.dll` for the target platform (x86/x64)
     - `Resources\` if any required resource files (e.g., `guest_private_key.pem`) are referenced by code
     - `scripts\` (optional, for install helpers)

4. Run locally (console mode) for testing
   - Running in console mode is recommended before installing the service. Some projects expose a `--console` or similar flag; otherwise run the exe directly and observe console output:
     - `SshTunnelService.exe --console`
   - Verify:
     - SSH connection to the configured SshHost/SshPort as SshUser
     - Tunnel bindings succeed and are logged
     - No permission or format errors in the Tunnels configuration

5. Install as a Windows Service
   - Two recommended methods: `install_service.ps1` script (provided) or use NSSM (Non-Sucking Service Manager).

   Using the provided PowerShell script:
   - Open an elevated PowerShell prompt (Run as Administrator).
   - From the installation folder run:
     - `.\scripts\install_service.ps1` (inspect the script to see expected command line parameters; the script commonly calls `sc create`, configures the binary path and start type).
   - To start the service:
     - `sc start SshTunnelService` (or the service name configured in the script).

   Using NSSM (recommended if you want easier stdout/stderr capture and restart configuration):
   - Download NSSM from https://nssm.cc/ and install it.
   - Install service with NSSM setting path to the exe and parameters (if any).
   - Use NSSM GUI or command line to redirect stdout/stderr to log files.

6. Uninstall the service
   - Use the provided `scripts\uninstall_service.ps1` script or `sc delete <ServiceName>`.
   - If NSSM was used: `nssm remove <ServiceName> confirm`.

---

Scripts included and purpose

- scripts\install_service.ps1
  - PowerShell helper to create the Windows service (likely uses `sc.exe`). Inspect the script before running and run with Administrator privileges.
- scripts\test_tunnel.ps1
  - Convenience script to test that a remote forwarded port connects through to the local service (likely opens a TCP connection to the remote host:port or runs a loopback check).
- scripts\uninstall_service.ps1
  - PowerShell helper to stop and remove the Windows service.

Always inspect scripts before executing, adapt any paths and service names to match the deployed environment, and run them from an elevated shell.

---

Logging & database

- DbLoggingService.cs implements logging into a local SQLite database. The Database folder likely contains the schema or is the runtime location for the DB file.
- The database stores events such as:
  - Service start/stop
  - Tunnel up/down events
  - SSH errors and exception traces
  - Reconnect attempts
- Location & rotation:
  - Verify the code or configuration to determine the default DB file location (commonly a file in the Database folder or the application working directory).
  - Back up or rotate database files as required for production environments.
- Diagnostic output:
  - When running in console mode, logging may be printed to the console. When running as a service, logs are written to the database and/or redirected files (if using NSSM).

---

Files requiring special attention

- Resources/guest_private_key.pem
  - This example private key is included for testing only. Replace with a secure private key or configure the code to use a key located on disk with restricted NTFS permissions.
  - NEVER commit production private keys to source control.
- Lib/SQLite/SQLite.Interop.dll (x86/x64)
  - Ensure the deployed process bitness matches the interop DLL. If the service runs as an x64 process, deploy the x64 interop. If running as x86, deploy the x86 interop.
- Renci.SshNet.dll
  - Ensure the correct version is present; if updating SSH.NET, test session behavior and recompile if necessary.

---

Equivalent SSH command examples (for understanding)

A reverse tunnel established by the service is equivalent to an OpenSSH command like:

OpenSSH (example):
ssh -i C:\path\to\id_rsa -N -R 127.0.0.1:2222:127.0.0.1:3389 -o ServerAliveInterval=60 -o ServerAliveCountMax=3 remoteuser@ssh.example.com -p 22

PuTTY/plink (example):
"C:\Program Files\PuTTY\plink.exe" -i "C:\path\to\id_rsa.ppk" -N -batch -R 0.0.0.0:2222:127.0.0.1:3389 remoteuser@ssh.example.com -P 22

Notes:
- `-N` prevents execution of remote commands (tunnel-only).
- `-R remoteHost:remotePort:localHost:localPort` requests reverse forwarding.
- Keepalive options (ServerAliveInterval / CountMax) help detect dead peers and recover sessions.

---

Troubleshooting checklist

- Authentication failures ("Permission denied (publickey)")
  - Verify the private key file format and location.
  - Confirm the public key is in `~/.ssh/authorized_keys` on the remote server and has correct permissions.
  - Ensure the service account has read access to the private key file if the service uses a local file.
- Tunnels fail to bind (port already in use)
  - Check if the remote port is free on the SSH server (use `ss -ltnp` or `netstat -tulpn`).
  - Ensure the configured remoteHost/remotePort are valid and not in use by other services.
- Remote bind refused (GatewayPorts)
  - If binding to `0.0.0.0` or any non-loopback address, ensure the server has `GatewayPorts yes` enabled in `/etc/ssh/sshd_config`.
  - Confirm `AllowTcpForwarding yes` on server side.
- Service fail to start or crashes
  - Run the application in console mode to observe exceptions.
  - Check the SQLite DB logs (DbLoggingService) or Windows Event Viewer for errors.
  - Confirm all native dependencies (SQLite interop) are deployed for correct architecture.
- Intermittent disconnects
  - Increase `HeartbeatIntervalMs` or configure keepalive settings in code (if exposed). Inspect SshTunnelManager.cs for parameters controlling reconnect behavior.

---

Security best practices

- Use key-based authentication and protect private keys with tight NTFS ACLs. Use a least-privilege service account.
- Do not leave example keys (like guest_private_key.pem) in deployment. Replace and remove the example key.
- Limit the remote SSH account used by the tunnel: consider restricting shell access, enforce a forced command, or isolate via firewall rules.
- Audit the SSH server for tunnel usage and log connection events.
- Rotate keys regularly and remove compromised or unused keys from `authorized_keys`.
- When exposing services via `GatewayPorts`, put additional protections in front of them (firewall rules, authentication proxies, or VPN).

---

Development notes & where to look in the source

- Core/Models/TunnelDefinition.cs
  - Defines the data model for a single tunnel. Review it to understand fields, validation and defaulting behavior.
- Core/Services/SshTunnelManager.cs
  - Contains the main logic: SSH session creation via Renci.SshNet, reverse-forward requests, monitoring loop, and reconnect/backoff logic. Modify here to tune behavior (backoff, keepalive, reconnect strategy).
- Infrastructure/DbLoggingService.cs
  - Implements persistence of logs into SQLite. Review for DB schema, file location and retention logic.
- ServiceHost/Program.cs and TunnelService.cs
  - Service bootstrap and Windows service implementation. Review for service name, event logging to Windows Event Log, and console-mode behavior.
- scripts/*.ps1
  - Installation helpers that should be customized for production environment, service name consistency, and ACL setup for key files.

---

License

- See LICENSE in the repository. The distribution provided is packaged with a license file (commonly MIT in similar project distributions). Confirm the repository's LICENSE file for exact terms.

---

Final notes and next steps

- Before deploying to production:
  - Replace the example private key, double-check App.config values and ensure the correct SQLite interop DLL (x86/x64) is packaged with the deployed binary.
  - Test the service in an isolated environment using the `test_tunnel.ps1` script and by running the binary in console mode.
  - Inspect and, if necessary, adapt `SshTunnelManager.cs` reconnect logic to match operational needs (increase backoff, add jitter, limit retries).
- If building from a newer Visual Studio, permit migration of project files and re-test package restore and runtime behavior.
- Keep secrets out of the repository and verify service account privileges and logging strategy.

This README describes the repository contents and usage in detail. Refer to the indicated source files for implementation specifics and adjust configuration and deployment steps to fit the target environment.
