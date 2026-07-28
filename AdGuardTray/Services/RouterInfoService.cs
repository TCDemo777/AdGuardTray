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
                        "LC_ALL=C top -bn1 | head -5");

                // BusyBox/OpenWrt commonly reports either:
                //   CPU:  3% usr  2% sys ... 95% idle
                // or:
                //   %Cpu(s): ... 95.0 id
                var idleMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        cpu,
                        @"(?<idle>\d+(?:[\.,]\d+)?)\s*%?\s*(?:idle|id)\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (idleMatch.Success &&
                    double.TryParse(
                        idleMatch.Groups["idle"].Value.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double idle))
                {
                    info.CpuUsage =
                        Math.Round(
                            Math.Clamp(100 - idle, 0, 100),
                            1) + "%";
                }
                else
                {
                    info.CpuUsage = "-";
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
                        "free -k | awk '/^Mem:/ {print $2, $3, $6, $7}'");

                string[] parts =
                    memory.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3 &&
                    double.TryParse(parts[0], out double total) &&
                    double.TryParse(parts[1], out double used) &&
                    double.TryParse(parts[2], out double cache) &&
                    total > 0)
                {
                    double usedPercent =
                        Math.Clamp(
                            (used / total) * 100,
                            0,
                            100);

                    info.MemoryUsage =
                        Math.Round(
                            usedPercent,
                            1) + "%";

                    info.MemoryUsed =
                        FormatKilobytes(used);

                    info.MemoryCache =
                        FormatKilobytes(cache);
                }
                else
                {
                    info.MemoryUsage = "-";
                    info.MemoryUsed = "-";
                    info.MemoryCache = "-";
                }
            }
            catch
            {
                info.MemoryUsage = "-";
                info.MemoryUsed = "-";
                info.MemoryCache = "-";
            }



            //
            // Storage
            //

            info.StorageUsage =
                (await _ssh.RunCommandAsync(
                    "df -h / | tail -1")).Trim();


            return info;
        }

        private static string FormatKilobytes(
            double kilobytes)
        {
            double megabytes =
                kilobytes / 1024d;

            if (megabytes >= 1024d)
            {
                return
                    $"{megabytes / 1024d:0.0} GB";
            }

            return
                $"{megabytes:0} MB";
        }

    }
}