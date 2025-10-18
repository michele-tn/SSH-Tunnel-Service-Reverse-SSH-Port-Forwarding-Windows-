```markdown
# SSH Tunnel Service — Reverse SSH Port Forwarding for Windows

A lightweight Windows service that establishes and maintains reverse SSH tunnels (remote port forwarding) to a remote SSH server. The service is implemented for Windows (built against .NET Framework as indicated by app.config) and is designed to run headless as a Windows Service. It monitors and restarts SSH tunnels automatically, keeping connections alive across transient network failures and reboots.

This README describes repository layout, configuration options (app.config and example JSON), installation, usage, examples, troubleshooting, logging, security considerations and development notes.

Table of contents
- Project overview
- Features
- Requirements
- How it works
- Repository layout
- Configuration
  - app.config (primary)
  - config_example.json (optional / historical)
  - Tunnels format and examples
- Installation (Windows service)
  - Install with sc.exe
  - Install with NSSM
  - Example PowerShell helper
- Running in console mode (for testing)
- Examples (OpenSSH / PuTTY plink)
- Logging and monitoring
- Troubleshooting
- Security considerations
- Development notes
- Contributing
- License
- Appendix: Recommended SSH server settings

---

Project overview

This service connects to a remote SSH server and requests reverse-forward bindings from specified remote ports on that server to local ports on the Windows host. Each configured tunnel maps remoteHost:remotePort on the SSH server to localHost:localPort on the Windows machine. The service monitors the SSH client process and re-establishes tunnels when the connection drops, using an exponential backoff strategy configurable by the user.

Typical uses:
- Expose a local web server, RDP, VNC or other TCP service through a central SSH server.
- Provide remote maintenance access to machines behind NAT or strict firewalls.
- Ensure persistent remote access with automatic reconnection after network interruptions.

Features

- Persistent reverse SSH tunnels (ssh -R semantics).
- Automatic reconnect with configurable backoff and jitter.
- Key-based SSH authentication support.
- Support for OpenSSH clients (ssh) and optional PuTTY/plink.
- Configurable keepalive/heartbeat settings.
- Windows Service integration (automatic startup).
- Console mode for local debugging and validation.
- Logging to file with rotation hints.

Requirements

- Windows 10 / Windows Server 2016 or later (or other supported Windows versions compatible with .NET Framework 4.8.1).
- OpenSSH client (Windows 10+ includes it) or PuTTY/plink if using .ppk keys.
- Remote SSH server with these settings enabled (see Appendix):
  - AllowTcpForwarding yes
  - GatewayPorts yes (if binding non-loopback addresses on the remote server is needed)
- A valid SSH account on the remote server. Private key-based authentication is recommended for unattended services.

How it works

1. On startup the service reads configuration from app.config (see "Configuration" below).
2. For each configured tunnel the service builds the appropriate SSH command-line (OpenSSH or plink) to request reverse forwarding (ssh -R).
3. The service launches an SSH client process and monitors its lifetime.
4. On unexpected disconnect, the service waits according to reconnect policy and restarts the process. Events and errors are logged.

Repository layout (expected)

The repository is organized to separate binaries, source, scripts and documentation. The actual tree may vary; this section documents the expected layout and the files referenced in this README.

- README.md                            <- This file
- LICENSE                              <- License file (MIT)
- CHANGELOG.md                         <- Optional changelog
- config_example.json                  <- Example JSON configuration (optional)
- app.config                           <- Application configuration used by the compiled service
- /bin/
  - SSHReverseTunnelService.exe         <- Compiled Windows executable (release) or equivalent binary
- /src/                                <- Source code (C# project files)
  - (source files)
- /scripts/
  - install_service.ps1                <- PowerShell helper to create service with sc.exe (optional)
  - install_nssm.ps1                   <- PowerShell helper to install using NSSM (optional)
  - run_console.ps1                    <- Start executable in console mode with chosen config (optional)
- /docs/                               <- Additional documentation (design, troubleshooting)
- /logs/                               <- Example log directory (created at runtime)
- .gitignore

Notes:
- The compiled binary is normally distributed in releases or placed in /bin/ for local deployment. Source code is under /src/.
- Keep sensitive files (private keys, config containing secrets) out of the repository or add them to .gitignore.

Configuration

This project uses app.config (XML) as its primary configuration source at runtime. A JSON example is provided in the repository for convenience or for alternative tools; however the service reads the app.config settings shown below by default.

app.config (example)

The app.config used in the project contains keys that control SSH connection details, tunnel definitions, heartbeat and maximum tunnels. An example app.config in the repository:

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

app.config keys (explanation)
- SshHost
  - The hostname or IP address of the remote SSH server (e.g. ssh.example.com or 203.0.113.10).
- SshPort
  - TCP port on the remote SSH server (default 22 if not customized).
- SshUser
  - Username used to authenticate to the SSH server (e.g. tunneluser).
- MaxTunnels
  - Maximum number of concurrent tunnels the service should manage. If more tunnels are provided in Tunnels the service may limit the active ones up to this value.
- HeartbeatIntervalMs
  - Interval in milliseconds for an internal heartbeat/monitor loop (controls how often the service checks tunnel status).
- Tunnels
  - Comma-separated list of tunnel definitions in the format:
    remoteHost:remotePort:localHost:localPort
  - Example:
    127.0.0.1:2222:127.0.0.1:3389,0.0.0.0:8080:127.0.0.1:80
    This config requests two reverse forwards:
      * remote 127.0.0.1:2222 -> local 127.0.0.1:3389
      * remote 0.0.0.0:8080 -> local 127.0.0.1:80 (requires GatewayPorts on the server)

config_example.json (optional)
A JSON example was previously provided in the repository to illustrate a richer configuration schema (keeps logging, reconnect and keepalive fields). If the project or a fork supports JSON configuration, consult the source to confirm the runtime precedence. Example content (kept here for completeness):

```json
{
  "ssh": {
    "host": "ssh.example.com",
    "port": 22,
    "user": "remoteuser",
    "privateKeyPath": "C:\\ProgramData\\ssh-keys\\id_rsa",
    "privateKeyPassphrase": "",
    "usePlink": false,
    "plinkPath": "C:\\Program Files\\PuTTY\\plink.exe",
    "additionalOptions": []
  },
  "tunnels": [
    {
      "name": "rdp-tunnel",
      "remoteHost": "0.0.0.0",
      "remotePort": 2222,
      "localHost": "127.0.0.1",
      "localPort": 3389
    },
    {
      "name": "http-tunnel",
      "remoteHost": "127.0.0.1",
      "remotePort": 8080,
      "localHost": "127.0.0.1",
      "localPort": 80
    }
  ],
  "service": {
    "name": "SSHReverseTunnelService",
    "runAsUser": "LocalSystem",
    "startOnBoot": true
  },
  "reconnect": {
    "initialDelaySeconds": 5,
    "maxDelaySeconds": 300,
    "backoffFactor": 2.0,
    "jitterSeconds": 5
  },
  "keepalive": {
    "serverAliveInterval": 60,
    "serverAliveCountMax": 3
  },
  "logging": {
    "logPath": "C:\\ProgramData\\SSHReverseTunnelService\\logs\\service.log",
    "level": "info",
    "maxLogSizeMB": 20,
    "maxBackupFiles": 7
  }
}
```

Note: If both app.config and a JSON config are present, verify how the compiled binary chooses the configuration source (the code in /src/ will show precedence). By default the .NET app reads app.config unless explicitly implemented to read JSON.

Tunnels format details and constraints
- remoteHost:
  - `127.0.0.1` — bind only to the loopback interface on the SSH server.
  - `0.0.0.0` — bind to all interfaces on the SSH server (requires GatewayPorts yes).
  - A specific IP address may be used when the server has multiple interfaces and binding to one is required.
- remotePort:
  - Port on the SSH server that will accept incoming connections and forward them to the Windows host.
  - Ensure this port is not blocked by the server firewall and not already bound by another process.
- localHost/localPort:
  - The destination on the Windows host receiving the forwarded traffic (commonly `127.0.0.1` and the local service port).
- Example mapping:
  - Tunnels = 127.0.0.1:2222:127.0.0.1:3389
    - Resulting SSH option (OpenSSH equivalent): -R 127.0.0.1:2222:127.0.0.1:3389

Installation (Windows service)

Before installing:
- Place the executable (SSHReverseTunnelService.exe or the built binary) and app.config in a folder accessible by the service account, e.g.:
  - C:\Program Files\SSHReverseTunnelService\
- Ensure private key files (if used) are present and the service account has read access.

Install service using sc.exe (built-in)
1. Open an elevated PowerShell or Command Prompt (Run as Administrator).
2. Create the service (example). Adjust paths and arguments to reflect the actual binary name and required arguments:

```powershell
sc create "SSHReverseTunnelService" binPath= "\"C:\Program Files\SSHReverseTunnelService\SSHReverseTunnelService.exe\" --service" start= auto DisplayName= "SSH Reverse Tunnel Service"
sc start "SSHReverseTunnelService"
```

Notes:
- The binary may not need a --service flag depending on how the executable handles service vs console modes. Confirm by reading the source or help output.
- To run the service as a specific user (for access to keys stored in user profile), use the `obj= "<DOMAIN\User>"` and set `password` via `sc` or configure later using Services MMC.

Install service using NSSM (recommended for robust stdout/stderr and restart handling)
1. Download NSSM from https://nssm.cc/ and extract it.
2. From an elevated prompt:

```powershell
nssm install SSHReverseTunnelService "C:\Program Files\SSHReverseTunnelService\SSHReverseTunnelService.exe" --service
```

3. Configure the Arguments field (if required) and AppDirectory in NSSM GUI. In the I/O tab redirect stdout/stderr to files to capture the SSH client output.
4. Configure restart behavior and start the service:
```powershell
nssm start SSHReverseTunnelService
```

Install using the included PowerShell helper (if present)
- If the repository contains scripts/install_service.ps1, run the script with elevated privileges:
```powershell
.\scripts\install_service.ps1 -InstallPath "C:\Program Files\SSHReverseTunnelService" -ConfigPath "C:\Program Files\SSHReverseTunnelService\app.config"
```
- The helper may wrap sc.exe or NSSM steps and adjust ACLs for key files.

Running in console mode (testing and debugging)

Before installing as a service run the executable in console mode to validate configuration and key permissions. Example:

```powershell
"C:\Program Files\SSHReverseTunnelService\SSHReverseTunnelService.exe" --console --config "C:\Program Files\SSHReverseTunnelService\app.config"
```

Console mode prints diagnostic logs, helps verify:
- Private key access and format (OpenSSH vs .ppk).
- SSH server reachability and authentication.
- Tunnel binding success (port already in use errors).
- Command-line argument syntax differences (ssh vs plink).

Examples — Equivalent SSH commands

The service will construct SSH/plink commands equivalent to the following examples.

OpenSSH (single tunnel):
```powershell
ssh -i "C:\keys\id_rsa" -N -R 127.0.0.1:2222:127.0.0.1:3389 -o ServerAliveInterval=60 -o ServerAliveCountMax=3 remoteuser@ssh.example.com -p 22
```

PuTTY / plink (single tunnel, .ppk key):
```powershell
"C:\Program Files\PuTTY\plink.exe" -i "C:\keys\id_rsa.ppk" -N -batch -R 0.0.0.0:2222:127.0.0.1:3389 remoteuser@ssh.example.com -P 22
```

Important flags:
- -N: do not execute remote commands (tunnel only).
- -R remoteHost:remotePort:localHost:localPort: request reverse forwarding.
- -o ServerAliveInterval / -o ServerAliveCountMax: keepalive options for OpenSSH.
- -batch: recommended for plink to avoid interactive prompts in unattended contexts.

Logging and monitoring

- The project writes events to log files. The location and rotation parameters may be configurable either in app.config or in a JSON config if supported by the compiled binary.
- Recommended log contents:
  - Service lifecycle events (start, stop, restart).
  - SSH command lines (with sensitive fields redacted — do not log private key contents or passphrases).
  - Tunnel bind successes and failures.
  - Reconnect attempts and backoff values.
  - Errors from the SSH client and exit codes.
- Rotation strategy:
  - Use a scheduled task or a system tool to rotate/compress logs and keep a bounded history (e.g., daily rotation with 7 backups).
  - Alternatively, use Event Viewer for high-level service status and file logs for trace-level diagnostics.
- Health checks:
  - Scripted external check that attempts to open a TCP connection to the remoteHost:remotePort to verify the tunnel is available.
  - Monitor the Windows service status with standard monitoring tools.

Troubleshooting

- Permission denied (publickey)
  - Confirm private key path is correct and readable by the service account.
  - Confirm the public key exists in `~/.ssh/authorized_keys` on the remote server and has correct permissions.
  - If the key is encrypted with a passphrase, arrange for an agent or a passphrase-less key (with secure storage) for unattended service operation.
- Remote port already in use
  - Choose another remotePort. On the server, inspect listeners via `ss -ltnp` or `netstat -tulpn`.
- Remote bind refused (GatewayPorts disabled)
  - On the SSH server check `/etc/ssh/sshd_config` and ensure `AllowTcpForwarding yes` and, if external binding is required, `GatewayPorts yes`. Restart sshd after changes.
- Intermittent disconnects
  - Increase keepalive/heartbeat settings and adjust reconnect backoff/jitter to avoid quick repeated restarts.
- Service fails to start
  - Run the binary directly in console mode to reveal exceptions during startup.
  - Check Windows Event Viewer for service-related failure codes.
  - Confirm service account has rights to access files (keys, config) and network.
- Firewall or outbound blocking
  - Ensure the Windows host can reach the SSH server on the configured port.
  - On the server, ensure binding/listening on the requested remote ports is allowed through its firewall.

Security considerations

- Use key-based authentication instead of passwords for unattended operation. Protect private keys with NTFS ACLs and limit read access to the service account.
- Consider a dedicated SSH account on the remote server with minimal privileges. When possible restrict that account to only allow port forwarding (e.g., with a restricted command or via firewall rules).
- Rotate SSH keys regularly and revoke compromised keys immediately on the server.
- Do not embed private keys in the repository. Place them on the host with strict permissions and document their expected location in deployment guides.
- Use logging and auditing on the SSH server to track incoming connections and remote bind usage.
- If exposing services to the public internet via GatewayPorts, add additional protections such as authentication proxies, network ACLs, or VPNs.

Development notes

- The project targets .NET Framework 4.8.1 (app.config supported runtime). Confirm the version in /src/ project files if building from source.
- If modifying configuration behavior (e.g., adding JSON parsing or environmental variable overrides), include robust validation and redaction of sensitive fields in logs.
- Add unit/integration tests for configuration parsing, command-line generation (OpenSSH vs plink), and reconnect/backoff logic.

Contributing

- Contributions are welcome via pull requests. When opening issues or PRs include:
  - Clear steps to reproduce the problem.
  - Relevant configuration excerpts (redact secrets).
  - Log snippets demonstrating the issue (redact sensitive data).
- Follow existing code style in /src/ and include tests for new functionality.
- Update CHANGELOG.md and increment versioning per repository conventions.

License

This project is licensed under the MIT License. See LICENSE for details.

Appendix: Recommended SSH server settings

On the remote SSH server (typically /etc/ssh/sshd_config) verify these settings as needed for reverse forwarding:

```
AllowTcpForwarding yes
# GatewayPorts no   # default; set to yes if remote tunnels must bind to non-loopback addresses
GatewayPorts yes
TCPKeepAlive yes
ClientAliveInterval 60
ClientAliveCountMax 3
```

After changes:
- sudo systemctl restart sshd

Final notes

- Review the source in /src/ to confirm exact command-line flags, the precedence of config sources (app.config vs JSON), and any additional runtime options supported by the binary.
- Keep secrets out of version control and limit service account privileges to the minimum required for operation.
- Use the repository Issues tab to report bugs or request features; include the configuration, logs and reproduction steps.

```
```
