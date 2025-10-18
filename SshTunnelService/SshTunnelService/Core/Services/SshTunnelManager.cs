using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Renci.SshNet;
using SshTunnelService.Core.Models;
using SshTunnelService.Infrastructure;

namespace SshTunnelService.Core.Services
{
    public class SshTunnelManager
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _privateKeyResourceName; // resource manifest name
        private readonly List<TunnelDefinition> _tunnels;
        private readonly DbLoggingService _logger;
        private readonly int _maxTunnels;
        private readonly int _heartbeatIntervalMs;

        private SshClient _client;
        private Thread _worker;
        private CancellationTokenSource _cts;

        public SshTunnelManager(string host, int port, string username, string privateKeyResourceName, List<TunnelDefinition> tunnels, DbLoggingService logger, int maxTunnels = 5, int heartbeatIntervalMs = 30000)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentNullException("host");
            if (string.IsNullOrEmpty(username)) throw new ArgumentNullException("username");
            if (string.IsNullOrEmpty(privateKeyResourceName)) throw new ArgumentNullException("privateKeyResourceName");
            if (logger == null) throw new ArgumentNullException("logger");

            _host = host;
            _port = port;
            _username = username;
            _privateKeyResourceName = privateKeyResourceName;
            _tunnels = tunnels ?? new List<TunnelDefinition>();
            _logger = logger;
            _maxTunnels = Math.Max(1, Math.Min(5, maxTunnels));
            _heartbeatIntervalMs = heartbeatIntervalMs;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _worker = new Thread(() => RunAsync(_cts.Token)) { IsBackground = true, Name = "SshTunnelManagerWorker" };
            _worker.Start();
            _logger.Info("SshTunnelManager", "Manager started");
        }

        public void Stop(int timeoutMillis = 30000)
        {
            if (_cts == null) return;
            _logger.Info("SshTunnelManager", "Shutdown requested");
            _cts.Cancel();

            try
            {
                if (_worker != null && _worker.IsAlive)
                {
                    if (timeoutMillis <= 0) _worker.Join();
                    else _worker.Join(timeoutMillis);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("SshTunnelManager", "Exception while waiting for worker: " + ex.Message, ex);
            }
            finally
            {
                Disconnect();
                try { _cts.Dispose(); } catch { }
                _cts = null;
                _worker = null;
                _logger.Info("SshTunnelManager", "Manager stopped");
            }
        }

        private void RunAsync(CancellationToken token)
        {
            int attempt = 0;
            var rnd = new Random();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    ConnectAndStart(token);
                    attempt = 0;

                    while (!token.IsCancellationRequested && _client != null && _client.IsConnected)
                    {
                        _logger.Info("SshTunnelManager", "Heartbeat: connection alive");
                        for (int i = 0; i < _heartbeatIntervalMs / 1000; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            Thread.Sleep(1000);
                        }
                    }

                    if (token.IsCancellationRequested) break;

                    _logger.Info("SshTunnelManager", "Connection lost, will attempt reconnect");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("SshTunnelManager", "Exception in manager loop: " + ex.Message, ex);
                }

                attempt++;
                int delay = Math.Min(120000, (int)(Math.Pow(2, attempt) * 1000)) + rnd.Next(0, 1000);
                _logger.Info("SshTunnelManager", string.Format("Reconnecting after {0} ms (attempt {1})", delay, attempt));

                var waited = 0;
                while (waited < delay && !token.IsCancellationRequested)
                {
                    Thread.Sleep(500);
                    waited += 500;
                }
            }
        }

        private Stream GetEmbeddedPrivateKeyStream()
        {
            var assembly = Assembly.GetExecutingAssembly();
            // expected resource name: SshTunnelService.Resources.guest_private_key.pem
            var stream = assembly.GetManifestResourceStream(_privateKeyResourceName);
            return stream;
        }

        private void ConnectAndStart(CancellationToken token)
        {
            Disconnect();
            token.ThrowIfCancellationRequested();

            using (var keyStream = GetEmbeddedPrivateKeyStream())
            {
                if (keyStream == null)
                {
                    throw new FileNotFoundException("Embedded private key not found: " + _privateKeyResourceName);
                }

                var keyFile = new PrivateKeyFile(keyStream);
                var conn = new ConnectionInfo(_host, _port, _username, new PrivateKeyAuthenticationMethod(_username, keyFile));
                _client = new SshClient(conn);
                _client.Connect();
                _logger.Info("SshTunnelManager", string.Format("Connected to {0}:{1}", _host, _port));

                int started = 0;
                foreach (var t in _tunnels)
                {
                    if (token.IsCancellationRequested) break;
                    if (started >= _maxTunnels) break;

                    var fwd = new ForwardedPortRemote(t.RemoteHost, t.RemotePort, t.LocalHost, t.LocalPort);
                    _client.AddForwardedPort(fwd);
                    fwd.Start();
                    started++;
                    _logger.Info("SshTunnelManager", string.Format("Tunnel started: {0}:{1} -> {2}:{3}", t.RemoteHost, t.RemotePort, t.LocalHost, t.LocalPort));
                }

                _logger.Info("SshTunnelManager", string.Format("{0} tunnels active", started));
            }
        }

        private void Disconnect()
        {
            try
            {
                if (_client == null) return;

                try
                {
                    foreach (var p in _client.ForwardedPorts)
                    {
                        var f = p as ForwardedPortRemote;
                        if (f != null && f.IsStarted)
                        {
                            try { f.Stop(); } catch { }
                        }
                    }
                }
                catch { }

                if (_client.IsConnected) _client.Disconnect();
                _client.Dispose();
                _client = null;
                _logger.Info("SshTunnelManager", "Disconnected");
            }
            catch (Exception ex)
            {
                _logger.Error("SshTunnelManager", "Error during disconnect: " + ex.Message, ex);
            }
        }
    }
}
