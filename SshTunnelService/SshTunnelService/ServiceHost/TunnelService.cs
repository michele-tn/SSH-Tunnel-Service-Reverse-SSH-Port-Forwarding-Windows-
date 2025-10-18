using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.ServiceProcess;
using SshTunnelService.Core.Models;
using SshTunnelService.Core.Services;
using SshTunnelService.Infrastructure;
using System;


namespace SshTunnelService
{
    public class TunnelService : ServiceBase
    {
        private SshTunnelManager _manager;
        private DbLoggingService _logger;

        public TunnelService()
        {
            ServiceName = "SshTunnelService";
        }

        protected override void OnStart(string[] args)
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "Logs.db");
            _logger = new DbLoggingService(dbPath);

            string host = ConfigurationManager.AppSettings["SshHost"];
            int port = int.Parse(ConfigurationManager.AppSettings["SshPort"] ?? "3422");
            string user = ConfigurationManager.AppSettings["SshUser"];
            int maxTunnels = int.Parse(ConfigurationManager.AppSettings["MaxTunnels"] ?? "5");
            int heartbeat = int.Parse(ConfigurationManager.AppSettings["HeartbeatIntervalMs"] ?? "30000");
            string tunnelsSetting = ConfigurationManager.AppSettings["Tunnels"];

            // parse tunnels
            var tunnels = new List<TunnelDefinition>();
            if (!string.IsNullOrWhiteSpace(tunnelsSetting))
            {
                var entries = tunnelsSetting.Split(new[] {','}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var e in entries)
                {
                    var parts = e.Split(new[] {':'}, System.StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 4) continue;
                    uint remotePort; uint localPort;
                    uint.TryParse(parts[1], out remotePort);
                    uint.TryParse(parts[3], out localPort);
                    tunnels.Add(new TunnelDefinition(parts[0], remotePort, parts[2], localPort));
                }
            }

            // embedded resource manifest name
            string keyResource = "SshTunnelService.Resources.guest_private_key.pem";

            _manager = new SshTunnelManager(host, port, user, keyResource, tunnels, _logger, maxTunnels, heartbeat);
            _manager.Start();
            _logger.Info("TunnelService", "Service started");
        }

        protected override void OnStop()
        {
            _logger.Info("TunnelService", "Service stopping");
            _manager?.Stop(30000);
            _logger.Info("TunnelService", "Service stopped");
        }
    }
}
