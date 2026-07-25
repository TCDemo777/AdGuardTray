using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public class RouterManager
    {
        private readonly GLInetSshService _ssh;

        private readonly string _routerIp;

        private readonly RouterInfoService _routerInfo;

        private readonly NetworkService _network;

        public RouterManager(
     string routerIp,
     string username,
     string password)
        {
            _routerIp = routerIp;

            _ssh =
                new GLInetSshService(
                    routerIp,
                    username,
                    password);

            _routerInfo =
                new RouterInfoService(_ssh);

            _network =
                new NetworkService(_ssh);
        }

        //
        // Router
        //

        public Task<RouterInfo> GetRouterInfoAsync()
        {
            return _routerInfo.GetRouterInfoAsync();
        }

        //
        // Network
        //

        public Task<NetworkInfo> GetNetworkInfoAsync()
        {
            return _network.GetNetworkInfoAsync();
        }

        //
        // AdGuard
        //

        public async Task<AdGuardStatus> GetAdGuardStatusAsync()
        {
            string service =
                await _ssh.RunCommandAsync(
                    "/etc/init.d/adguardhome status");

            if (service.StartsWith("SSH_"))
            {
                return new AdGuardStatus
                {
                    IsRunning = false,
                    ServiceStatus = service
                };
            }

            string process =
                await _ssh.RunCommandAsync(
                    "pgrep -a AdGuardHome");

            string version =
                await _ssh.RunCommandAsync(
                    "/usr/bin/AdGuardHome --version");

            return new AdGuardStatus
            {
                IsRunning =
                    service.Contains("running"),

                ServiceStatus =
                    service.Trim(),

                Process =
                    string.IsNullOrWhiteSpace(process)
                        ? "Not Running"
                        : process.Trim(),

                Version =
                    version.Trim()
            };
        }

        //
        // AdGuard Statistics
        //

        public async Task<AdGuardStatistics> GetAdGuardStatisticsAsync()
        {
            var stats =
                new AdGuardStatistics();


            try
            {
                using HttpClient client =
                    new HttpClient();


                string url =
                    $"http://{_routerIp}:3000/control/stats";


                string json =
                    await client.GetStringAsync(url);


                using JsonDocument doc =
                    JsonDocument.Parse(json);


                JsonElement root =
                    doc.RootElement;


                if (root.TryGetProperty(
                    "num_dns_queries",
                    out JsonElement queries))
                {
                    stats.TotalQueries =
                        queries.GetInt32();
                }


                if (root.TryGetProperty(
                    "num_blocked_filtering",
                    out JsonElement blocked))
                {
                    stats.BlockedQueries =
                        blocked.GetInt32();
                }
            }
            catch (Exception ex)
            {
                stats.TotalQueries = -1;
                stats.BlockedQueries = -1;
            }


            return stats;
        }


        //
        // Controls
        //

        public Task StartAdGuardAsync()
        {
            return _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome start");
        }

        public Task StopAdGuardAsync()
        {
            return _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome stop");
        }

        public Task RestartAdGuardAsync()
        {
            return _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome restart");
        }

        public Task EnableAdGuardAsync()
        {
            return _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome enable");
        }

        public Task DisableAdGuardAsync()
        {
            return _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome disable");
        }

        //
        // Logs
        //

        public async Task<string> GetLogsAsync()
        {
            return await _ssh.RunCommandAsync(
                "logread -e AdGuardHome");
        }

        //
        // Reboot
        //

        public Task RebootRouterAsync()
        {
            return _ssh.RunCommandAsync(
                "reboot");
        }
    }
}