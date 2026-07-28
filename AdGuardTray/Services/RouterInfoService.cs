using System;
using System.Text.Json;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public class RouterInfoService
    {
        private readonly GLInetSshService _ssh;


        public RouterInfoService(GLInetSshService ssh)
        {
            _ssh = ssh;
        }


        public async Task<RouterInfo> GetRouterInfoAsync()
        {
            var info = new RouterInfo();


            //
            // Board information
            //

            string boardJson =
                await _ssh.RunCommandAsync(
                    "ubus call system board");


            try
            {
                using JsonDocument doc =
                    JsonDocument.Parse(boardJson);


                JsonElement root =
                    doc.RootElement;


                if (root.TryGetProperty(
                    "model",
                    out JsonElement model))
                {
                    info.Model =
                        model.GetString() ?? "-";
                }


                if (root.TryGetProperty(
                    "hostname",
                    out JsonElement hostname))
                {
                    info.Hostname =
                        hostname.GetString() ?? "-";
                }


                if (root.TryGetProperty(
                    "release",
                    out JsonElement release))
                {
                    if (release.TryGetProperty(
                        "version",
                        out JsonElement version))
                    {
                        info.Firmware =
                            version.GetString() ?? "-";
                    }
                }
            }
            catch
            {
                info.Model = "Unknown";
            }



            //
            // Uptime
            //

            try
            {
                string uptimeSeconds =
                    await _ssh.RunCommandAsync(
                        "cat /proc/uptime | awk '{print $1}'");


                if (double.TryParse(
                    uptimeSeconds.Trim(),
                    out double seconds))
                {
                    TimeSpan uptime =
                        TimeSpan.FromSeconds(seconds);


                    if (uptime.TotalDays >= 1)
                    {
                        info.Uptime =
                            $"{(int)uptime.TotalDays} days " +
                            $"{uptime.Hours} hours " +
                            $"{uptime.Minutes} minutes";
                    }
                    else
                    {
                        info.Uptime =
                            $"{uptime.Hours} hours " +
                            $"{uptime.Minutes} minutes";
                    }
                }
                else
                {
                    info.Uptime = "-";
                }
            }
            catch
            {
                info.Uptime = "-";
            }


            //
            // CPU
            //

            try
            {
                string cpu =
                    await _ssh.RunCommandAsync(
                        "top -bn1 | grep CPU");


                int idleIndex =
                    cpu.IndexOf("idle");


                if (idleIndex > 0)
                {
                    string idleText =
                        cpu.Substring(
                            0,
                            idleIndex);


                    string[] parts =
                        idleText.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries);


                    foreach (string part in parts)
                    {
                        if (part.EndsWith("%"))
                        {
                            if (double.TryParse(
                                part.Replace("%", ""),
                                out double idle))
                            {
                                info.CpuUsage =
                                    Math.Round(
                                        100 - idle,
                                        1) + "%";
                            }

                            break;
                        }
                    }
                }
            }
            catch
            {
                info.CpuUsage = "-";
            }



            //
            // Memory
            //

            try
            {
                string memory =
                    await _ssh.RunCommandAsync(
                        "free | grep Mem");


                string[] parts =
                    memory.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);


                if (parts.Length >= 3)
                {
                    double total =
                        double.Parse(parts[1]);

                    double used =
                        double.Parse(parts[2]);


                    double percent =
                        (used / total) * 100;


                    info.MemoryUsage =
                        Math.Round(
                            percent,
                            1) + "%";
                }
            }
            catch
            {
                info.MemoryUsage = "-";
            }



            //
            // Storage
            //

            info.StorageUsage =
                (await _ssh.RunCommandAsync(
                    "df -h / | tail -1")).Trim();


            return info;
        }
    }
}