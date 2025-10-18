using System;

namespace SshTunnelService.Core.Models
{
    public class TunnelDefinition
    {
        public string RemoteHost { get; set; }
        public uint RemotePort { get; set; }
        public string LocalHost { get; set; }
        public uint LocalPort { get; set; }

        public TunnelDefinition() { }

        public TunnelDefinition(string remoteHost, uint remotePort, string localHost, uint localPort)
        {
            RemoteHost = remoteHost;
            RemotePort = remotePort;
            LocalHost = localHost;
            LocalPort = localPort;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1} -> {2}:{3}", RemoteHost, RemotePort, LocalHost, LocalPort);
        }
    }
}
