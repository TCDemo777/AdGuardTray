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
            _ssh =
                new GLInetSshService(
                    routerIp,
                    username,
                    password);
        }





        public async Task<AdGuardStatus> GetAdGuardStatusAsync()
        {
            try
            {
                string service =
                    await _ssh.RunCommandAsync(
                        "/etc/init.d/adguardhome status");



                if (IsSshError(service))
                {
                    return CreateErrorStatus(service);
                }



                string process =
                    await _ssh.RunCommandAsync(
                        "pgrep -a AdGuardHome");



                if (IsSshError(process))
                {
                    return CreateErrorStatus(process);
                }



                string version =
                    await _ssh.RunCommandAsync(
                        "/usr/bin/AdGuardHome --version");



                if (IsSshError(version))
                {
                    return CreateErrorStatus(version);
                }



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
            catch (Exception ex)
            {
                return new AdGuardStatus
                {
                    IsRunning = false,

                    ServiceStatus =
                        "ERROR",

                    Process =
                        ex.Message,

                    Version =
                        ""
                };
            }
        }





        public async Task StartAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome start");
        }





        public async Task StopAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome stop");
        }





        public async Task RestartAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome restart");
        }





        public async Task EnableAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome enable");
        }





        public async Task DisableAdGuardAsync()
        {
            await _ssh.RunCommandAsync(
                "/etc/init.d/adguardhome disable");
        }





        public async Task<string> GetLogsAsync()
        {
            return await _ssh.RunCommandAsync(
                "logread -e AdGuardHome");
        }





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





        public async Task RebootRouterAsync()
        {
            await _ssh.RunCommandAsync(
                "reboot");
        }





        private bool IsSshError(
            string result)
        {
            return
                result == "SSH_AUTH_FAILED" ||
                result == "SSH_CONNECTION_FAILED" ||
                result == "SSH_NETWORK_FAILED" ||
                result.StartsWith(
                    "SSH_ERROR:",
                    StringComparison.OrdinalIgnoreCase);
        }





        private AdGuardStatus CreateErrorStatus(
            string error)
        {
            string message =
                error switch
                {
                    "SSH_AUTH_FAILED" =>
                        "SSH authentication failed",

                    "SSH_CONNECTION_FAILED" =>
                        "Router connection failed",

                    "SSH_NETWORK_FAILED" =>
                        "Network unavailable",

                    _ =>
                        error
                };


            return new AdGuardStatus
            {
                IsRunning = false,

                ServiceStatus =
                    message,

                Process =
                    "",

                Version =
                    ""
            };
        }
    }
}