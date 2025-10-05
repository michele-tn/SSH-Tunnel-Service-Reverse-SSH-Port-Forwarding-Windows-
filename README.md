# SSH Tunnel Service — Reverse SSH Port Forwarding (Windows)

## Abstract

This document describes an independently deployable Windows service implemented in C# (.NET 6) that establishes and maintains reverse SSH tunnels to a remote server. The service is intended for operational scenarios that require persistent, secure remote access to internal endpoints via reverse port forwarding. The implementation emphasizes modularity, resilience, testability, and maintainability, and it is accompanied by configuration-driven operation and extensibility hooks for logging and resource management.

---

## Contents

1. Project overview
2. Rationale and design goals
3. Project structure
4. Configuration
5. Runtime behaviour and lifecycle
6. Installation and execution
7. Test and development workflow
8. Key implementation details
9. Security considerations
10. Operational considerations
11. Extensibility and future work
12. Files included
13. License and attribution

---

## 1. Project overview

**SSH Tunnel Service** is a background service that connects to a remote SSH server using a private key (embedded as an application resource) and configures one or more reverse port forwards. The service automatically reestablishes connectivity should a transient network failure occur. Configuration is externally provided via a JSON file and the service enforces an administratively configurable maximum number of concurrent reverse tunnels (default: 5).

Primary objectives:
- Provide a reliable mechanism to expose local services through a remote SSH server using reverse forwarding.
- Minimize operational overhead by automating reconnection and tunnel management.
- Maintain a clear separation between control logic, configuration, resource handling, and logging to facilitate testing and maintenance.

---

## 2. Rationale and design goals

This project follows several software engineering principles:
- **Single Responsibility and Modularity**: Each module has one responsibility (configuration loading, resource loading, SSH management, logging).
- **Dependency Injection**: Interfaces abstract external concerns to enable unit testing and replacement of implementations.
- **Resilience**: Reconnection policy implements exponential backoff and jitter to handle unstable networks gracefully without tight loops.
- **Observability**: Structured logging to file and Windows Event Log enables operational debugging and incident analysis.
- **Configurability**: Operational variables (endpoints, tunnels, maximum allowed tunnels) are editable without recompilation.

These principles guide the project structure and the code-level separation of concerns described below.

---

## 3. Project structure

A representative project layout is presented below. The layout intentionally separates domain logic (Core), infrastructure concerns, and the host-service layer.

```
SshTunnelService/
├── src/
│   ├── Program.cs                         # Host configuration and DI composition
│   ├── ServiceHost/
│   │   └── TunnelWorker.cs                # BackgroundService/worker that hosts controller
│   ├── Core/
│   │   ├── Controllers/
│   │   │   ├── ISshTunnelController.cs
│   │   │   └── SshTunnelController.cs     # Primary control loop and tunnel lifecycle
│   │   ├── Policies/
│   │   │   └── ReconnectionPolicy.cs      # Exponential backoff + jitter
│   │   └── Models/
│   │       └── SshModels.cs               # Configuration models and validation
│   ├── Infrastructure/
│   │   ├── Config/
│   │   │   ├── IConfigProvider.cs
│   │   │   └── JsonConfigProvider.cs      # Reads appsettings.json
│   │   ├── Logging/
│   │   │   ├── ILogger.cs
│   │   │   └── FileEventLogger.cs         # Writes to filesystem and EventLog
│   │   ├── Resources/
│   │   │   ├── IResourceLoader.cs
│   │   │   └── EmbeddedResourceLoader.cs  # Loads embedded private key resource
│   │   └── Ssh/
│   │       ├── ISshClientWrapper.cs
│   │       └── SshNetClientWrapper.cs     # Adapts Renci.SshNet for testability
│   └── appsettings.json
├── scripts/
│   ├── install-service.ps1
│   └── uninstall-service.ps1
└── README.md (this document)
```

---

## 4. Configuration

All runtime parameters are provided via `appsettings.json` in the application working directory. The service expects the JSON structure to follow the `SshSettings` model. Minimal example:

```json
{
  "Ssh": {
    "Host": "5.250.191.95",
    "Port": 3422,
    "Username": "guest",
    "PrivateKeyEmbeddedResource": "PowerToys.Resources.mikey_private.pem",
    "Tunnels": [
      {
        "RemoteHost": "127.0.0.1",
        "RemotePort": 9389,
        "LocalHost": "127.0.0.1",
        "LocalPort": 3389
      },
      {
        "RemoteHost": "127.0.0.1",
        "RemotePort": 9922,
        "LocalHost": "127.0.0.1",
        "LocalPort": 22
      }
    ],
    "MaxTunnels": 5
  },
  "Logging": {
    "LogFolder": "logs"
  }
}
```

### Validation rules
- `MaxTunnels` enforces an upper bound at startup. If the configuration specifies more than `MaxTunnels`, the service refuses to start and writes an explanatory log entry.
- Each tunnel mapping must include `RemotePort` and `LocalPort` as integers and may optionally specify hosts. Default host is `127.0.0.1` if omitted.

---

## 5. Runtime behaviour and lifecycle

### Start-up
1. The host process reads configuration via the `IConfigProvider` implementation.
2. The service validates configuration constraints (e.g., maximum tunnels).
3. The controller is initialized (with logger and resource loader) and started as a background thread or hosted background service.

### Connection and tunnel establishment
- The controller loads the private key from the embedded resource using `IResourceLoader` and creates an SSH client using a wrapper for `Renci.SshNet`.
- For each configured tunnel mapping, the controller registers a `ForwardedPortRemote` instance with the SSH client and starts the forwarded port.

### Monitoring and reconnection
- The controller runs a supervisory loop that verifies the client connection and the status of the forwarded ports.
- If the client is disconnected, the controller attempts to reconnect according to the reconnection policy (exponential backoff with jitter). On successful reconnection, the configured tunnels are recreated.

### Shutdown
- On service stop, the controller cancels the supervisory loop, stops all forwarded ports, disconnects the SSH client, and releases resources.

---

## 6. Installation and execution

### Development / test execution
To run in test mode (console), execute the project with an interactive host. This mode writes logs to the configured log folder and to the console:

```bash
dotnet run --project src/SshTunnelService.csproj
```

### Windows service installation (administrative privileges required)
A PowerShell script is provided for convenience:

```powershell
# Run as Administrator
./scripts/install-service.ps1 -exePath "C:/Path/To/SshTunnelService.exe"
```
To uninstall the service:

```powershell
./scripts/uninstall-service.ps1
```

Alternatively, the service may be installed using `sc.exe`:

```powershell
sc create SshTunnelService binPath= "C:/Path/To/SshTunnelService.exe" start= auto DisplayName= "SSH Tunnel Service"
sc start SshTunnelService
```

---

## 7. Test and development workflow

- Use dependency injection to provide test doubles for `ISshClientWrapper`, `IResourceLoader`, and `IConfigProvider`.
- Unit tests should validate: configuration parsing and validation, reconnection policy behavior, resource-loading failure modes, and that the controller attempts to reestablish tunnels after simulated disconnects.
- Integration tests may run a local SSH server (or containerized SSH instance) to verify forwarded port behaviour end-to-end.

---

## 8. Key implementation details

### Embedded key handling
- The embedded private key is loaded via `Assembly.GetManifestResourceStream` (or equivalent through `IResourceLoader`).
- The code reads the key into memory and constructs a `PrivateKeyFile` object for use with `Renci.SshNet`.
- The recommended key format is OpenSSH PEM. PuTTY PPK files should be converted prior to embedding:
  ```powershell
  puttygen key.ppk -O private-openssh -o key.pem
  ```

### SSH client wrapper
- The `ISshClientWrapper` interface isolates external code from the concrete SSH library, simplifying unit testing and future library replacement.

### Reconnection policy
- The `ReconnectionPolicy` exposes configurable parameters for initial delay and maximum delay and implements an exponential backoff with bounded jitter to avoid thundering herd effects.

---

## 9. Security considerations

- **Private key confidentiality**: embedding the key in the binary reduces exposure through file-system access but does not substitute for secret management in high-security environments. Consider using a secrets manager (e.g., Azure Key Vault, HashiCorp Vault) or Windows DPAPI if key rotation or stricter access controls are required.
- **Least privilege**: run the service under a least-privilege account that has access strictly to the required local resources. Avoid running as SYSTEM if not necessary.
- **SSH server configuration**: ensure that the remote SSH server permits remote port forwarding (`AllowTcpForwarding yes`) and configure `GatewayPorts` according to the intended visibility (local vs. global).
- **Audit and monitoring**: integrate log collection and alerting for repeated reconnection events, unexpected tunnel closures, or configuration changes.

---

## 10. Operational considerations

- **Resource usage**: each active forwarded port consumes resources; the configured upper bound (default 5) imposes a practical constraint to avoid uncontrolled resource consumption.
- **Network implications**: reverse tunnels expose local services on remote ports. Ensure that firewall policies, NAT behavior, and remote server port usage are coordinated to prevent collisions and unauthorized exposure.
- **Startup failure**: if configuration validation fails (e.g., more tunnels than permitted), the service will log a descriptive error and stop. Monitor Event Log and configured log files during first deployment.

---

## 11. Extensibility and future work

Potential enhancements include:
- Support for dynamic reloading of configuration without restart (file watchers with atomic replacement).
- Integration with secret management systems for private key retrieval at runtime.
- Health endpoint exposing metrics and status for monitoring systems (Prometheus, etc.).
- Optional SSH keep-alive or periodic probe traffic for improved failure detection.
- Optional support for different tunnel types (local-forwarding, dynamic SOCKS forwarding).
- Graceful rolling update facility for key rotation and configuration changes.

---

## 12. Files included

- Source files under `src/` (Core, Infrastructure, ServiceHost)
- `appsettings.json` example
- `scripts/install-service.ps1`, `scripts/uninstall-service.ps1`
- This `README.md`

---

## 13. License and attribution

This repository contains original implementation code and configuration examples. Choose and apply an appropriate license for your organizational requirements (for example, MIT, Apache 2.0, or a proprietary license).

---

## Contact and maintenance

This project is organized for maintainability and handoff. For operational deployment, assign an owner responsible for:
- Validating configuration prior to deployment.
- Managing private key lifecycle.
- Monitoring service logs and system resource usage.

---
