using System;
using System.Configuration;

using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using SshTunnelService.Core.Models;
using SshTunnelService.Core.Services;
using SshTunnelService.Infrastructure;

namespace SshTunnelService
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                RunInteractive();
            }
            else
            {
                ServiceBase.Run(new TunnelService());
            }
        }

        private static List<TunnelDefinition> ParseTunnels(string tunnelsSetting)
        {
            var list = new List<TunnelDefinition>();
            if (string.IsNullOrWhiteSpace(tunnelsSetting)) return list;

            // format: remoteHost:remotePort:localHost:localPort,remoteHost2:remotePort2:localHost2:localPort2
            var entries = tunnelsSetting.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var e in entries)
            {
                var parts = e.Split(new[] {':'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4) continue;
                uint remotePort, localPort;
                uint.TryParse(parts[1], out remotePort);
                uint.TryParse(parts[3], out localPort);
                list.Add(new TunnelDefinition(parts[0], remotePort, parts[2], localPort));
            }
            return list;
        }

        private static void RunInteractive()
        {
            Console.Title = "SSH Tunnel Service (Interactive Mode)";
            Console.WriteLine("=== SSH Tunnel Service (Interactive Mode) ===");
            Console.WriteLine("Press Q to stop. Logs are written to Database/Logs.db in the app folder.");
            Console.WriteLine();

            var dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "Logs.db");
            using (var logger = new DbLoggingService(dbPath))
            {
                string host = ConfigurationManager.AppSettings["SshHost"];
                int port = int.Parse(ConfigurationManager.AppSettings["SshPort"] ?? "3422");
                string user = ConfigurationManager.AppSettings["SshUser"];
                int maxTunnels = int.Parse(ConfigurationManager.AppSettings["MaxTunnels"] ?? "5");
                int heartbeat = int.Parse(ConfigurationManager.AppSettings["HeartbeatIntervalMs"] ?? "30000");
                string tunnelsSetting = ConfigurationManager.AppSettings["Tunnels"];

                var tunnels = ParseTunnels(tunnelsSetting);

                // embedded resource manifest name
                string keyResource = "SshTunnelService.Resources.guest_private_key.pem";

                var manager = new SshTunnelManager(host, port, user, keyResource, tunnels, logger, maxTunnels, heartbeat);
                manager.Start();

                while (true)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q)
                    {
                        logger.Info("Program", "Interactive stop requested");
                        manager.Stop(10000);
                        break;
                    }
                }

                logger.Info("Program", "Interactive exited");
                Console.WriteLine("Exited interactive mode.");
            }
        }
    }
}
