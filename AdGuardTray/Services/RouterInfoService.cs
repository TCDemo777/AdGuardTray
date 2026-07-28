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
                        "awk '/^(MemTotal|MemFree|Buffers|Cached|SReclaimable):/ {print $1 $2}' /proc/meminfo");

                double total = 0;
                double free = 0;
                double buffers = 0;
                double cached = 0;
                double reclaimable = 0;

                foreach (string line in
                    memory.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] pair =
                        line.Split(
                            ':',
                            StringSplitOptions.RemoveEmptyEntries);

                    if (pair.Length != 2 ||
                        !double.TryParse(
                            pair[1].Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double value))
                    {
                        continue;
                    }

                    switch (pair[0].Trim())
                    {
                        case "MemTotal":
                            total = value;
                            break;
                        case "MemFree":
                            free = value;
                            break;
                        case "Buffers":
                            buffers = value;
                            break;
                        case "Cached":
                            cached = value;
                            break;
                        case "SReclaimable":
                            reclaimable = value;
                            break;
                    }
                }

                double cache =
                    cached + reclaimable;

                double used =
                    Math.Max(
                        0,
                        total - free - buffers - cache);

                if (total > 0)
                {
                    info.MemoryUsage =
                        Math.Round(
                            used / total * 100,
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

            return megabytes >= 1024d
                ? $"{megabytes / 1024d:0.0} GB"
                : $"{megabytes:0} MB";
        }

    }
}