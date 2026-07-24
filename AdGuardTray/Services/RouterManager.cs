using System;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public class RouterManager
    {
        private readonly GLInetSshService _ssh;

        public RouterManager(
            string routerIp,
            string username,
            string password)
        {
            _ssh = new GLInetSshService(
                routerIp,
                username,
                password);
        }

        /// <summary>
        /// Gets the current AdGuard Home status.
        /// </summary>
        public async Task<AdGuardStatus> GetAdGuardStatusAsync()
        {
            string service =
                await _ssh.RunCommandAsync(
                    "/etc/init.d/adguardhome status");

            string process =
                await _ssh.RunCommandAsync(
                    "pgrep -a AdGuardHome");

            string version =
                await _ssh.RunCommandAsync(
                    "/usr/bin/AdGuardHome --version");

            return new AdGuardStatus
            {
                IsRunning =
                    service.Contains(
                        "running",
                        StringComparison.OrdinalIgnoreCase),

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

        /// <summary>
        /// Starts AdGuard Home.
        /// </summary>
        public async Task StartAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome start");
        }

        /// <summary>
        /// Stops AdGuard Home.
        /// </summary>
        public async Task StopAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome stop");
        }

        /// <summary>
        /// Restarts AdGuard Home.
        /// </summary>
        public async Task RestartAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome restart");
        }

        /// <summary>
        /// Enables AdGuard Home to start automatically.
        /// </summary>
        public async Task EnableAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome enable");
        }

        /// <summary>
        /// Disables AdGuard Home from starting automatically.
        /// </summary>
        public async Task DisableAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome disable");
        }

        /// <summary>
        /// Retrieves the AdGuard Home log.
        /// </summary>
        public async Task<string> GetLogsAsync()
        {
            return await _ssh.RunCommandAsync(
                "logread -e AdGuardHome");
        }

        /// <summary>
        /// Gets basic router information.
        /// </summary>
        public async Task<string> GetRouterInfoAsync()
        {
            string board =
                await _ssh.RunCommandAsync(
                    "ubus call system board");

            string uptime =
                await _ssh.RunCommandAsync(
                    "uptime");

            string memory =
                await _ssh.RunCommandAsync(
                    "free -h");

            string disk =
                await _ssh.RunCommandAsync(
                    "df -h");

            return
                "===== Router Information =====\r\n\r\n" +

                "System Board\r\n" +
                "------------\r\n" +
                board +

                "\r\n\r\nUptime\r\n" +
                "------\r\n" +
                uptime +

                "\r\n\r\nMemory\r\n" +
                "------\r\n" +
                memory +

                "\r\n\r\nDisk Usage\r\n" +
                "----------\r\n" +
                disk;
        }

        /// <summary>
        /// Reboots the router.
        /// </summary>
        public async Task RebootRouterAsync()
        {
            await _ssh.RunCommandAsync(
                "reboot");
        }
    }
}