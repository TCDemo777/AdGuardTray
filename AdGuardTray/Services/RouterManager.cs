using System;
using System.Net;
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
        private readonly string _adminToken;
        private readonly RouterInfoService _routerInfo;
        private readonly NetworkService _network;

        public RouterManager(
            string routerIp,
            string username,
            string password,
            string adminToken)
        {
            _routerIp = routerIp;
            _adminToken = adminToken;

            _ssh = new GLInetSshService(
                routerIp,
                username,
                password);

            _routerInfo = new RouterInfoService(_ssh);
            _network = new NetworkService(_ssh);
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
        // AdGuard Status
        //

        public async Task<AdGuardStatus> GetAdGuardStatusAsync()
        {
            string service = await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome status");

            string process = await _ssh.RunCommandAsync(
                "pgrep -a AdGuardHome");

            string version = await _ssh.RunCommandAsync(
                "/usr/bin/AdGuardHome --version");

            return new AdGuardStatus
            {
                IsRunning = service.Contains(
                    "running",
                    StringComparison.OrdinalIgnoreCase),

                ServiceStatus = service.Trim(),

                Process = string.IsNullOrWhiteSpace(process)
                    ? "Not Running"
                    : process.Trim(),

                Version = version.Trim()
            };
        }

        //
        // AdGuard Statistics
        //

        public async Task<AdGuardStatistics> GetAdGuardStatisticsAsync()
        {
            var stats = new AdGuardStatistics();

            try
            {
                if (string.IsNullOrWhiteSpace(_adminToken))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "AdGuard Admin-Token has not been configured.");

                    stats.TotalQueries = -1;
                    stats.BlockedQueries = -1;

                    return stats;
                }

                var cookieContainer = new CookieContainer();

                cookieContainer.Add(
                    new Uri($"http://{_routerIp}:3000"),
                    new Cookie(
                        "Admin-Token",
                        _adminToken,
                        "/"));

                using var handler = new HttpClientHandler
                {
                    CookieContainer = cookieContainer,
                    UseCookies = true,
                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "application/json");

                string url =
                    $"http://{_routerIp}:3000/control/stats";

                System.Diagnostics.Debug.WriteLine(
                    "Calling AdGuard stats: " + url);

                using HttpResponseMessage response =
                    await client.GetAsync(url);

                string json =
                    await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine(
                    "AdGuard status: " + response.StatusCode);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "The GL.iNet Admin-Token is missing or expired.");

                    stats.TotalQueries = -1;
                    stats.BlockedQueries = -1;

                    return stats;
                }

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "AdGuard response: " + json);

                    stats.TotalQueries = -1;
                    stats.BlockedQueries = -1;

                    return stats;
                }

                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root =
                    document.RootElement;

                if (root.TryGetProperty(
                    "num_dns_queries",
                    out JsonElement queries) &&
                    queries.TryGetInt32(out int totalQueries))
                {
                    stats.TotalQueries = totalQueries;
                }

                if (root.TryGetProperty(
                    "num_blocked_filtering",
                    out JsonElement blocked) &&
                    blocked.TryGetInt32(out int blockedQueries))
                {
                    stats.BlockedQueries = blockedQueries;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Queries: {stats.TotalQueries}");

                System.Diagnostics.Debug.WriteLine(
                    $"Blocked: {stats.BlockedQueries}");
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "The AdGuard statistics request timed out.");

                stats.TotalQueries = -1;
                stats.BlockedQueries = -1;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "AdGuard HTTP error: " + ex.Message);

                stats.TotalQueries = -1;
                stats.BlockedQueries = -1;
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "AdGuard JSON error: " + ex.Message);

                stats.TotalQueries = -1;
                stats.BlockedQueries = -1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "AdGuard statistics error: " + ex.Message);

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

        public Task<string> GetLogsAsync()
        {
            return _ssh.RunCommandAsync(
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