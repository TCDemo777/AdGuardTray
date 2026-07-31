using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public class RouterManager
    {
        private readonly GLInetSshService _ssh;
        private readonly GLInetSessionService _sessionService;
        private readonly string _routerIp;
        private readonly RouterInfoService _routerInfo;
        private readonly NetworkService _network;

        private readonly SemaphoreSlim _tokenLock =
            new SemaphoreSlim(1, 1);

        private string? _adminToken;

        public RouterManager(
            string routerIp,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(routerIp))
            {
                throw new ArgumentException(
                    "Router IP address cannot be empty.",
                    nameof(routerIp));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException(
                    "Username cannot be empty.",
                    nameof(username));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException(
                    "Password cannot be empty.",
                    nameof(password));
            }

            _routerIp =
                NormaliseRouterHost(
                    routerIp);

            _ssh =
                new GLInetSshService(
                    _routerIp,
                    username,
                    password);

            _sessionService =
                new GLInetSessionService(
                    _routerIp,
                    username,
                    password);

            _routerInfo =
                new RouterInfoService(
                    _ssh);

            _network =
                new NetworkService(
                    _ssh);
        }

        //
        // Router
        //

        public Task<RouterInfo> GetRouterInfoAsync()
        {
            return _routerInfo
                .GetRouterInfoAsync();
        }

        //
        // Network
        //

        public Task<NetworkInfo> GetNetworkInfoAsync()
        {
            return _network
                .GetNetworkInfoAsync();
        }

        public async Task<List<WifiRadioInfo>> GetWifiRadiosAsync()
        {
            // Read configured APs first.  GL.iNet's own client service is then
            // used as the primary station source because MediaTek firmware does
            // not consistently expose associations through iw/iwinfo.
            string networkCommand = """
                for s in $(uci show wireless 2>/dev/null | sed -n 's/^wireless\.\([^.=]*\)=wifi-iface$/\1/p'); do
                    mode=$(uci -q get wireless.$s.mode)
                    [ -z "$mode" -o "$mode" = "ap" ] || continue
                    dev=$(uci -q get wireless.$s.device)
                    [ -n "$dev" ] || continue
                    ssid=$(uci -q get wireless.$s.ssid)
                    [ -n "$ssid" ] || ssid='Hidden network'
                    band=$(uci -q get wireless.$dev.band)
                    [ -n "$band" ] || band=$(uci -q get wireless.$dev.hwmode)
                    channel=$(uci -q get wireless.$dev.channel)
                    [ -n "$channel" ] || channel='auto'
                    encryption=$(uci -q get wireless.$s.encryption)
                    [ -n "$encryption" ] || encryption='open'
                    disabled=$(uci -q get wireless.$s.disabled)
                    rdisabled=$(uci -q get wireless.$dev.disabled)
                    iface=''

                    for i in $(iw dev 2>/dev/null | awk '$1 == "Interface" { print $2 }'); do
                        runtime_ssid=$(iw dev "$i" info 2>/dev/null | sed -n 's/^[[:space:]]*ssid //p' | head -n1)
                        if [ "$runtime_ssid" = "$ssid" ]; then
                            iface="$i"
                            runtime_channel=$(iw dev "$i" info 2>/dev/null | awk '$1 == "channel" { print $2; exit }')
                            [ -n "$runtime_channel" ] && channel="$runtime_channel"
                            break
                        fi
                    done

                    state='Online'
                    [ "$disabled" = "1" -o "$rdisabled" = "1" ] && state='Disabled'
                    [ -z "$iface" -a "$state" = 'Online' ] && state='Configured'
                    display_iface="$iface"
                    [ -n "$display_iface" ] || display_iface="$dev"
                    printf 'N|%s|%s|%s|%s|%s|%s|%s|%s\n' "$s" "$dev" "$display_iface" "$ssid" "$band" "$channel" "$encryption" "$state"
                done
                """;

            string networkOutput = await _ssh.RunCommandAsync(networkCommand);
            var networks = new List<WifiRadioInfo>();

            foreach (string line in networkOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 9 || parts[0] != "N")
                {
                    continue;
                }

                string rawBand = parts[5].Trim().ToLowerInvariant();
                string band = rawBand.Contains("2g") || rawBand.Contains("11g") || rawBand.Contains("11b")
                    ? "2.4 GHz"
                    : rawBand.Contains("5g") || rawBand.Contains("11a") || rawBand.Contains("11ac") || rawBand.Contains("11ax")
                        ? "5 GHz"
                        : rawBand.Contains("6g")
                            ? "6 GHz"
                            : InferBandFromChannel(parts[6]);

                networks.Add(new WifiRadioInfo
                {
                    Radio = string.IsNullOrWhiteSpace(parts[2]) ? "-" : parts[2].Trim(),
                    Interface = string.IsNullOrWhiteSpace(parts[3]) ? "-" : parts[3].Trim(),
                    Ssid = string.IsNullOrWhiteSpace(parts[4]) ? "Hidden network" : parts[4].Trim(),
                    Band = band,
                    Channel = string.IsNullOrWhiteSpace(parts[6]) ? "auto" : parts[6].Trim(),
                    Security = FormatWifiSecurity(parts[7]),
                    Status = string.IsNullOrWhiteSpace(parts[8]) ? "Configured" : parts[8].Trim()
                });
            }

            if (networks.Count == 0)
            {
                return networks;
            }

            // GL.iNet firmware's client service knows the connection type even
            // where the MediaTek driver returns an empty station dump.
            string clientJson = await _ssh.RunCommandAsync(
                "ubus call gl-clients list 2>/dev/null || true");

            if (!string.IsNullOrWhiteSpace(clientJson))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(clientJson);
                    foreach (JsonElement client in EnumerateClientObjects(document.RootElement))
                    {
                        if (!GetFlexibleBoolean(client, "online", true))
                        {
                            continue;
                        }

                        string iface = GetFlexibleString(client, "iface", "interface", "connection", "type");
                        string band = NormaliseClientBand(iface);
                        if (band.Length == 0)
                        {
                            continue; // Cable/VPN clients do not belong to a Wi-Fi card.
                        }

                        WifiRadioInfo? network = FindClientNetwork(networks, client, band);
                        if (network == null)
                        {
                            continue;
                        }

                        string mac = GetFlexibleString(client, "mac", "macaddr", "mac_address");
                        if (mac.Length == 0 || network.Clients.Any(c =>
                                string.Equals(c.MacAddress, mac, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string name = GetFlexibleString(client, "name", "hostname", "host_name");
                        string ip = GetFlexibleString(client, "ip", "ipaddr", "ip_address");
                        string signal = GetFlexibleString(client, "signal", "rssi");

                        network.Clients.Add(new WifiClientInfo
                        {
                            Name = string.IsNullOrWhiteSpace(name) ? "Unknown device" : name,
                            IpAddress = string.IsNullOrWhiteSpace(ip) ? "-" : ip,
                            MacAddress = mac,
                            Signal = FormatSignal(signal),
                            Band = band,
                            Interface = network.Interface,
                            Ssid = network.Ssid
                        });
                    }
                }
                catch (JsonException)
                {
                    // Keep the configured networks visible.  Older firmware may
                    // briefly return an empty or incomplete gl-clients payload.
                }
            }

            return networks;
        }

        public async Task<List<WifiClientInfo>> GetGlClientInventoryAsync()
        {
            string clientJson = await _ssh.RunCommandAsync(
                "ubus call gl-clients list 2>/dev/null || true");

            var clients = new List<WifiClientInfo>();
            if (string.IsNullOrWhiteSpace(clientJson))
            {
                return clients;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(clientJson);
                foreach (JsonElement client in EnumerateClientObjects(document.RootElement))
                {
                    if (!GetFlexibleBoolean(client, "online", true))
                    {
                        continue;
                    }

                    string mac = GetFlexibleString(client, "mac", "macaddr", "mac_address");
                    if (string.IsNullOrWhiteSpace(mac) || clients.Any(item =>
                            item.MacAddress.Equals(mac, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string rawInterface = GetFlexibleString(
                        client, "iface", "interface", "connection", "type");
                    string band = NormaliseClientBand(rawInterface);
                    string ssid = GetFlexibleString(client, "ssid", "wifi", "network");
                    string name = GetFlexibleString(client, "name", "hostname", "host_name");
                    string ip = GetFlexibleString(client, "ip", "ipaddr", "ip_address");
                    string signal = GetFlexibleString(client, "signal", "rssi");

                    clients.Add(new WifiClientInfo
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "Unknown device" : name,
                        IpAddress = string.IsNullOrWhiteSpace(ip) ? "-" : ip,
                        MacAddress = mac,
                        Signal = FormatSignal(signal),
                        Band = string.IsNullOrWhiteSpace(band) ?
                            (rawInterface.Contains("cable", StringComparison.OrdinalIgnoreCase) ? "Ethernet" : "Unknown") : band,
                        Interface = string.IsNullOrWhiteSpace(rawInterface) ? "-" : rawInterface,
                        Ssid = string.IsNullOrWhiteSpace(ssid) ? "-" : ssid
                    });
                }
            }
            catch (JsonException)
            {
                // Return an empty inventory while leaving AdGuard client data usable.
            }

            return clients;
        }

        private static IEnumerable<JsonElement> EnumerateClientObjects(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                bool looksLikeClient =
                    HasAnyProperty(element, "mac", "macaddr", "mac_address") &&
                    HasAnyProperty(element, "iface", "interface", "connection", "type");

                if (looksLikeClient)
                {
                    yield return element;
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (JsonElement child in EnumerateClientObjects(property.Value))
                    {
                        yield return child;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (JsonElement child in EnumerateClientObjects(item))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static bool HasAnyProperty(JsonElement element, params string[] names)
        {
            return names.Any(name => TryGetPropertyIgnoreCase(element, name, out _));
        }

        private static string GetFlexibleString(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!TryGetPropertyIgnoreCase(element, name, out JsonElement value))
                {
                    continue;
                }

                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };
            }

            return string.Empty;
        }

        private static bool GetFlexibleBoolean(JsonElement element, string name, bool defaultValue)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out JsonElement value))
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
                JsonValueKind.String => value.GetString() is string text &&
                    (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     text.Equals("online", StringComparison.OrdinalIgnoreCase)),
                _ => defaultValue
            };
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string NormaliseClientBand(string iface)
        {
            string value = iface.Trim().ToLowerInvariant();
            if (value.Contains("2.4") || value.Contains("2g") || value.Contains("24g"))
            {
                return "2.4 GHz";
            }

            if (value.Contains("5g") || value.Contains("5 ghz") || value == "5")
            {
                return "5 GHz";
            }

            if (value.Contains("6g") || value.Contains("6 ghz") || value == "6")
            {
                return "6 GHz";
            }

            return string.Empty;
        }

        private static WifiRadioInfo? FindClientNetwork(
            List<WifiRadioInfo> networks,
            JsonElement client,
            string band)
        {
            string ssid = GetFlexibleString(client, "ssid", "wifi_name", "network");
            string runtimeInterface = GetFlexibleString(client, "ifname", "device", "wlan");

            if (ssid.Length > 0)
            {
                WifiRadioInfo? bySsid = networks.FirstOrDefault(n =>
                    n.Band == band && n.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
                if (bySsid != null)
                {
                    return bySsid;
                }
            }

            if (runtimeInterface.Length > 0)
            {
                WifiRadioInfo? byInterface = networks.FirstOrDefault(n =>
                    n.Band == band && n.Interface.Equals(runtimeInterface, StringComparison.OrdinalIgnoreCase));
                if (byInterface != null)
                {
                    return byInterface;
                }
            }

            // Firmware commonly reports only "2.4G" or "5G".  In that case
            // use the primary enabled AP for that band.
            return networks.FirstOrDefault(n =>
                n.Band == band && !n.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatSignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
            {
                return "-";
            }

            string value = signal.Trim();
            return value.Contains("dbm", StringComparison.OrdinalIgnoreCase)
                ? value
                : $"{value} dBm";
        }

        private static string FormatWifiSecurity(string encryption)
        {
            string value = encryption?.Trim().ToLowerInvariant() ?? string.Empty;
            if (value == "none" || value == "open") return "Open";
            if (value.Contains("sae") && value.Contains("psk")) return "WPA2 / WPA3";
            if (value.Contains("sae")) return "WPA3";
            if (value.Contains("psk2")) return "WPA2";
            if (value.Contains("psk")) return "WPA";
            return string.IsNullOrWhiteSpace(encryption) ? "Unknown" : encryption.Trim();
        }

        private static string InferBandFromChannel(string channelValue)
        {
            if (int.TryParse(channelValue?.Trim(), out int channel))
            {
                return channel <= 14 ? "2.4 GHz" : "5 GHz";
            }

            return "Unknown";
        }

        public async Task<string> RestartWifiAsync()
        {
            string result = await _ssh.RunCommandAsync("wifi reload >/tmp/adguardtray_wifi_reload.log 2>&1; rc=$?; echo $rc");
            return result.Trim().EndsWith("0", StringComparison.Ordinal)
                ? "Wi-Fi restart requested successfully."
                : "The router could not restart Wi-Fi.";
        }

        public async Task<string> RestartWanAsync()
        {
            string result = await _ssh.RunCommandAsync("ifdown wan >/dev/null 2>&1; sleep 2; ifup wan >/dev/null 2>&1; echo $?");
            return result.Trim().EndsWith("0", StringComparison.Ordinal)
                ? "WAN reconnect requested successfully."
                : "The router could not reconnect WAN.";
        }

        public async Task<NetworkTrafficSnapshot>
            GetNetworkTrafficSnapshotAsync()
        {
            // Resolve the physical device used by the logical WAN interface,
            // then read the kernel byte counters. The fallbacks cover common
            // GL.iNet/OpenWrt interface layouts.
            string output =
                await _ssh.RunCommandAsync(
                    "dev=$(ubus call network.interface.wan status 2>/dev/null | jsonfilter -e '@.l3_device' 2>/dev/null); " +
                    "[ -n \"$dev\" ] || dev=$(ubus call network.interface.wan status 2>/dev/null | jsonfilter -e '@.device' 2>/dev/null); " +
                    "[ -n \"$dev\" ] || dev=$(ip route show default 2>/dev/null | awk 'NR==1 {print $5}'); " +
                    "[ -n \"$dev\" ] || dev=eth1; " +
                    "rx=$(cat /sys/class/net/$dev/statistics/rx_bytes 2>/dev/null || echo 0); " +
                    "tx=$(cat /sys/class/net/$dev/statistics/tx_bytes 2>/dev/null || echo 0); " +
                    "printf '%s|%s|%s' \"$dev\" \"$rx\" \"$tx\"");

            string[] parts =
                output.Trim().Split('|');

            return new NetworkTrafficSnapshot
            {
                InterfaceName =
                    parts.Length > 0 &&
                    !string.IsNullOrWhiteSpace(parts[0])
                        ? parts[0].Trim()
                        : "-",

                ReceivedBytes =
                    parts.Length > 1 &&
                    long.TryParse(parts[1].Trim(), out long received)
                        ? received
                        : 0,

                TransmittedBytes =
                    parts.Length > 2 &&
                    long.TryParse(parts[2].Trim(), out long transmitted)
                        ? transmitted
                        : 0,

                CapturedAtUtc = DateTime.UtcNow
            };
        }

        //
        // AdGuard Status
        //

        public async Task<AdGuardStatus>
            GetAdGuardStatusAsync()
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


        //
        // AdGuard Protection
        //

        public async Task<AdGuardProtectionStatus>
            GetAdGuardProtectionStatusAsync()
        {
            string token =
                await GetAdminTokenAsync();

            AdGuardControlResponse response =
                await RequestAdGuardControlAsync(
                    HttpMethod.Get,
                    "status",
                    token);

            if (response.RequiresNewToken)
            {
                InvalidateAdminToken();

                token =
                    await GetAdminTokenAsync();

                response =
                    await RequestAdGuardControlAsync(
                        HttpMethod.Get,
                        "status",
                        token);
            }

            if (!response.IsSuccess)
            {
                throw CreateAdGuardControlException(
                    "read protection status",
                    response);
            }

            return ParseAdGuardProtectionStatus(
                response.Content);
        }

        public Task<AdGuardProtectionStatus>
            EnableProtectionAsync()
        {
            return SetAdGuardProtectionAsync(
                true,
                TimeSpan.Zero);
        }

        public Task<AdGuardProtectionStatus>
            ResumeProtectionAsync()
        {
            return SetAdGuardProtectionAsync(
                true,
                TimeSpan.Zero);
        }

        public Task<AdGuardProtectionStatus>
            DisableProtectionAsync()
        {
            return SetAdGuardProtectionAsync(
                false,
                TimeSpan.Zero);
        }

        public Task<AdGuardProtectionStatus>
            PauseProtectionAsync(
                TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    "Pause duration must be greater than zero.");
            }

            return SetAdGuardProtectionAsync(
                false,
                duration);
        }

        private async Task<AdGuardProtectionStatus>
            SetAdGuardProtectionAsync(
                bool enabled,
                TimeSpan duration)
        {
            long durationMilliseconds =
                enabled
                    ? 0
                    : Math.Max(
                        0,
                        (long)duration.TotalMilliseconds);

            string requestJson =
                JsonSerializer.Serialize(
                    new
                    {
                        enabled,
                        duration =
                            durationMilliseconds
                    });

            string token =
                await GetAdminTokenAsync();

            AdGuardControlResponse response =
                await RequestAdGuardControlAsync(
                    HttpMethod.Post,
                    "protection",
                    token,
                    requestJson);

            if (response.RequiresNewToken)
            {
                InvalidateAdminToken();

                token =
                    await GetAdminTokenAsync();

                response =
                    await RequestAdGuardControlAsync(
                        HttpMethod.Post,
                        "protection",
                        token,
                        requestJson);
            }

            if (!response.IsSuccess)
            {
                throw CreateAdGuardControlException(
                    enabled
                        ? "enable protection"
                        : "disable protection",
                    response);
            }

            return await GetAdGuardProtectionStatusAsync();
        }

        private async Task<AdGuardControlResponse>
            RequestAdGuardControlAsync(
                HttpMethod method,
                string endpoint,
                string token,
                string? json = null)
        {
            var cookieContainer =
                new CookieContainer();

            var adGuardBaseUri =
                new Uri(
                    $"http://{_routerIp}:3000");

            cookieContainer.Add(
                adGuardBaseUri,
                new Cookie(
                    "Admin-Token",
                    token,
                    "/"));

            using var handler =
                new HttpClientHandler
                {
                    CookieContainer =
                        cookieContainer,

                    UseCookies =
                        true,

                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout =
                        TimeSpan.FromSeconds(10)
                };

            client.DefaultRequestHeaders
                .Accept
                .ParseAdd(
                    "application/json");

            string safeEndpoint =
                endpoint.TrimStart('/');

            string url =
                $"http://{_routerIp}:3000/control/" +
                safeEndpoint;

            using var request =
                new HttpRequestMessage(
                    method,
                    url);

            if (json is not null)
            {
                // Some GL.iNet AdGuard Home builds reject
                // "application/json; charset=utf-8" and require the
                // Content-Type value to be exactly "application/json".
                //
                // StringContent automatically appends the charset, so
                // use ByteArrayContent and set the header explicitly.
                request.Content =
                    new ByteArrayContent(
                        System.Text.Encoding.UTF8
                            .GetBytes(
                                json));

                request.Content.Headers
                    .TryAddWithoutValidation(
                        "Content-Type",
                        "application/json");
            }

            Debug.WriteLine(
                $"Calling AdGuard {method}: {url}");

            using HttpResponseMessage response =
                await client.SendAsync(
                    request);

            string content =
                await response.Content
                    .ReadAsStringAsync();

            Debug.WriteLine(
                "AdGuard control status: " +
                $"{(int)response.StatusCode} " +
                response.StatusCode);

            return new AdGuardControlResponse(
                response.StatusCode,
                content);
        }

        private static AdGuardProtectionStatus
            ParseAdGuardProtectionStatus(
                string json)
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            JsonElement root =
                document.RootElement;

            bool enabled =
                root.TryGetProperty(
                    "protection_enabled",
                    out JsonElement enabledElement) &&
                enabledElement.ValueKind ==
                    JsonValueKind.True;

            long remainingMilliseconds =
                0;

            if (root.TryGetProperty(
                    "protection_disabled_duration",
                    out JsonElement durationElement))
            {
                if (!durationElement.TryGetInt64(
                        out remainingMilliseconds) &&
                    durationElement.TryGetDouble(
                        out double durationDouble))
                {
                    remainingMilliseconds =
                        (long)durationDouble;
                }
            }

            remainingMilliseconds =
                Math.Max(
                    0,
                    remainingMilliseconds);

            return new AdGuardProtectionStatus
            {
                IsEnabled =
                    enabled,

                IsPaused =
                    !enabled &&
                    remainingMilliseconds > 0,

                RemainingPause =
                    TimeSpan.FromMilliseconds(
                        remainingMilliseconds)
            };
        }

        private static Exception
            CreateAdGuardControlException(
                string action,
                AdGuardControlResponse response)
        {
            string detail =
                string.IsNullOrWhiteSpace(
                    response.Content)
                    ? "No response body was returned."
                    : response.Content.Trim();

            return new InvalidOperationException(
                $"Unable to {action}. " +
                $"AdGuard Home returned HTTP " +
                $"{(int)response.StatusCode} " +
                $"{response.StatusCode}. {detail}");
        }


        //
        // AdGuard Protection Management
        //

        public async Task<AdGuardProtectionOptions> GetProtectionOptionsAsync()
        {
            var filtering = await GetControlJsonAsync("filtering/status");
            var safeBrowsing = await GetControlJsonAsync("safebrowsing/status");
            var parental = await GetControlJsonAsync("parental/status");
            var safeSearch = await GetControlJsonAsync("safesearch/status");
            var queryLog = await GetControlJsonAsync("querylog/config");

            return new AdGuardProtectionOptions
            {
                FilteringEnabled = GetBoolean(filtering, "enabled"),
                FilteringIntervalHours = GetInteger(filtering, "interval", 24),
                SafeBrowsingEnabled = GetBoolean(safeBrowsing, "enabled"),
                ParentalEnabled = GetBoolean(parental, "enabled"),
                SafeSearchEnabled = GetBoolean(safeSearch, "enabled"),
                QueryLogEnabled = GetBoolean(queryLog, "enabled"),
                QueryLogAnonymizeClientIp = GetBoolean(queryLog, "anonymize_client_ip"),
                QueryLogInterval = GetDouble(queryLog, "interval", 24),
                QueryLogIgnored = GetStringArray(queryLog, "ignored"),
                SafeSearch = new AdGuardSafeSearchSettings
                {
                    Enabled = GetBoolean(safeSearch, "enabled"),
                    Bing = GetBoolean(safeSearch, "bing", true),
                    DuckDuckGo = GetBoolean(safeSearch, "duckduckgo", true),
                    Ecosia = GetBoolean(safeSearch, "ecosia", true),
                    Google = GetBoolean(safeSearch, "google", true),
                    Pixabay = GetBoolean(safeSearch, "pixabay", true),
                    Yandex = GetBoolean(safeSearch, "yandex", true),
                    YouTube = GetBoolean(safeSearch, "youtube", true)
                }
            };
        }

        public Task SetFilteringEnabledAsync(bool enabled) => SendControlJsonAsync(HttpMethod.Post, "filtering/config", JsonSerializer.Serialize(new { enabled, interval = 24 }));
        public Task SetSafeBrowsingEnabledAsync(bool enabled) => SendControlWithoutBodyAsync(HttpMethod.Post, enabled ? "safebrowsing/enable" : "safebrowsing/disable");
        public Task SetParentalEnabledAsync(bool enabled) => SendControlWithoutBodyAsync(HttpMethod.Post, enabled ? "parental/enable" : "parental/disable");

        public Task SetSafeSearchEnabledAsync(bool enabled, AdGuardSafeSearchSettings current)
        {
            string json = JsonSerializer.Serialize(new
            {
                enabled,
                bing = current.Bing,
                duckduckgo = current.DuckDuckGo,
                ecosia = current.Ecosia,
                google = current.Google,
                pixabay = current.Pixabay,
                yandex = current.Yandex,
                youtube = current.YouTube
            });
            return SendControlJsonAsync(HttpMethod.Put, "safesearch/settings", json);
        }

        public Task SetQueryLogEnabledAsync(bool enabled, AdGuardProtectionOptions current)
        {
            string json = JsonSerializer.Serialize(new
            {
                enabled,
                anonymize_client_ip = current.QueryLogAnonymizeClientIp,
                interval = current.QueryLogInterval <= 0 ? 24 : current.QueryLogInterval,
                ignored = current.QueryLogIgnored
            });
            return SendControlJsonAsync(HttpMethod.Put, "querylog/config/update", json);
        }

        public async Task<(List<BlockedServiceItem> Services, AdGuardBlockedServicesConfig Config)> GetBlockedServicesAsync()
        {
            JsonElement all =
                await GetControlJsonAsync(
                    "blocked_services/all");

            JsonElement configJson =
                await GetControlJsonAsync(
                    "blocked_services/get");

            var config =
                new AdGuardBlockedServicesConfig();

            if (configJson.TryGetProperty(
                    "schedule",
                    out JsonElement schedule))
            {
                config.ScheduleJson =
                    schedule.GetRawText();
            }

            foreach (string id in
                GetStringArray(
                    configJson,
                    "ids"))
            {
                config.EnabledIds.Add(id);
            }

            var result =
                new List<BlockedServiceItem>();

            JsonElement array =
                default;

            if (all.ValueKind ==
                JsonValueKind.Array)
            {
                array = all;
            }
            else if (all.ValueKind ==
                     JsonValueKind.Object)
            {
                // AdGuard Home versions expose the catalogue under either
                // "blocked_services" or "services".
                if (!all.TryGetProperty(
                        "blocked_services",
                        out array))
                {
                    all.TryGetProperty(
                        "services",
                        out array);
                }
            }

            if (array.ValueKind ==
                JsonValueKind.Array)
            {
                foreach (JsonElement item in
                    array.EnumerateArray())
                {
                    string id;
                    string name;

                    if (item.ValueKind ==
                        JsonValueKind.String)
                    {
                        id =
                            item.GetString()?.Trim() ??
                            string.Empty;

                        name =
                            FormatBlockedServiceName(id);
                    }
                    else if (item.ValueKind ==
                             JsonValueKind.Object)
                    {
                        id =
                            GetString(
                                item,
                                "id");

                        if (id.Length == 0)
                        {
                            id =
                                GetString(
                                    item,
                                    "service_id");
                        }

                        name =
                            GetString(
                                item,
                                "name");

                        if (name.Length == 0)
                        {
                            name =
                                GetString(
                                    item,
                                    "display_name");
                        }

                        if (name.Length == 0)
                        {
                            name =
                                FormatBlockedServiceName(id);
                        }
                    }
                    else
                    {
                        continue;
                    }

                    if (id.Length == 0 ||
                        result.Any(service =>
                            service.Id.Equals(
                                id,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    result.Add(
                        new BlockedServiceItem
                        {
                            Id = id,
                            Name = name,
                            Category = CategorizeBlockedService(id, name),
                            IsBlocked =
                                config.EnabledIds.Contains(id)
                        });
                }
            }

            return (result, config);
        }

        public Task UpdateBlockedServicesAsync(IEnumerable<string> ids, string scheduleJson)
        {
            JsonNode schedule = JsonNode.Parse(string.IsNullOrWhiteSpace(scheduleJson) ? "{}" : scheduleJson) ?? new JsonObject();
            var idArray = new JsonArray();
            foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                idArray.Add(id);
            }
            var root = new JsonObject { ["schedule"] = schedule, ["ids"] = idArray };
            return SendControlJsonAsync(HttpMethod.Put, "blocked_services/update", root.ToJsonString());
        }

        public async Task<List<CustomFilteringRule>> GetCustomFilteringRulesAsync()
        {
            JsonElement status = await GetControlJsonAsync("filtering/status");
            var result = new List<CustomFilteringRule>();
            if (status.TryGetProperty("user_rules", out JsonElement rules) && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in rules.EnumerateArray())
                {
                    string rule = item.GetString()?.Trim() ?? "";
                    if (rule.Length == 0) continue;
                    string type = rule.StartsWith("@@", StringComparison.Ordinal) ? "Allow" : rule.StartsWith("||", StringComparison.Ordinal) ? "Block" : "Custom";
                    result.Add(new CustomFilteringRule { Rule = rule, Type = type });
                }
            }
            return result;
        }

        public Task SetCustomFilteringRulesAsync(IEnumerable<string> rules) => SendControlJsonAsync(HttpMethod.Post, "filtering/set_rules", JsonSerializer.Serialize(new { rules = rules.ToArray() }));

        public async Task<List<DnsRewriteRule>> GetDnsRewritesAsync()
        {
            JsonElement root = await GetControlJsonAsync("rewrite/list");
            var result = new List<DnsRewriteRule>();
            JsonElement array = root.ValueKind == JsonValueKind.Array ? root : (root.TryGetProperty("rewrites", out JsonElement rewrites) ? rewrites : default);
            if (array.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in array.EnumerateArray()) result.Add(new DnsRewriteRule { Domain = GetString(item, "domain"), Answer = GetString(item, "answer") });
            return result;
        }

        public Task AddDnsRewriteAsync(string domain, string answer) => SendControlJsonAsync(HttpMethod.Post, "rewrite/add", JsonSerializer.Serialize(new { domain, answer }));
        public Task DeleteDnsRewriteAsync(string domain, string answer) => SendControlJsonAsync(HttpMethod.Post, "rewrite/delete", JsonSerializer.Serialize(new { domain, answer }));

        private async Task<JsonElement> GetControlJsonAsync(string endpoint)
        {
            AdGuardControlResponse response = await SendAuthenticatedControlAsync(HttpMethod.Get, endpoint, null);
            if (!response.IsSuccess) throw CreateAdGuardControlException("read " + endpoint, response);
            using JsonDocument document = JsonDocument.Parse(response.Content);
            return document.RootElement.Clone();
        }

        private async Task SendControlJsonAsync(HttpMethod method, string endpoint, string json)
        {
            AdGuardControlResponse response = await SendAuthenticatedControlAsync(method, endpoint, json);
            if (!response.IsSuccess) throw CreateAdGuardControlException("update " + endpoint, response);
        }

        private async Task SendControlWithoutBodyAsync(HttpMethod method, string endpoint)
        {
            AdGuardControlResponse response = await SendAuthenticatedControlAsync(method, endpoint, null);
            if (!response.IsSuccess) throw CreateAdGuardControlException("update " + endpoint, response);
        }

        private async Task<AdGuardControlResponse> SendAuthenticatedControlAsync(HttpMethod method, string endpoint, string? json)
        {
            string token = await GetAdminTokenAsync();
            AdGuardControlResponse response = await RequestAdGuardControlAsync(method, endpoint, token, json);
            if (response.RequiresNewToken)
            {
                InvalidateAdminToken();
                token = await GetAdminTokenAsync();
                response = await RequestAdGuardControlAsync(method, endpoint, token, json);
            }
            return response;
        }

        private static bool GetBoolean(JsonElement root, string name, bool fallback = false) => root.TryGetProperty(name, out JsonElement value) ? value.ValueKind == JsonValueKind.True : fallback;
        private static int GetInteger(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
        private static double GetDouble(JsonElement root, string name, double fallback) => root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result) ? result : fallback;
        private static string CategorizeBlockedService(
            string id,
            string name)
        {
            string value =
                $"{id} {name}".ToLowerInvariant();

            if (ContainsAny(value,
                    "playstation", "xbox", "steam", "epic-games",
                    "nintendo", "roblox", "battle.net", "ea ",
                    "gaming", "twitch"))
            {
                return "Gaming";
            }

            if (ContainsAny(value,
                    "netflix", "disney", "hulu", "prime-video",
                    "amazon-prime", "youtube", "vimeo", "dailymotion",
                    "streaming", "paramount", "peacock", "hbo"))
            {
                return "Streaming & Video";
            }

            if (ContainsAny(value,
                    "spotify", "soundcloud", "deezer", "tidal",
                    "apple-music", "music"))
            {
                return "Music";
            }

            if (ContainsAny(value,
                    "facebook", "instagram", "tiktok", "twitter",
                    "x.com", "snapchat", "pinterest", "reddit",
                    "linkedin", "social"))
            {
                return "Social Media";
            }

            if (ContainsAny(value,
                    "whatsapp", "telegram", "signal", "discord",
                    "messenger", "skype", "zoom", "teams",
                    "slack", "communication", "chat"))
            {
                return "Messaging & Meetings";
            }

            if (ContainsAny(value,
                    "dropbox", "onedrive", "google-drive", "icloud",
                    "cloud", "box.com"))
            {
                return "Cloud Storage";
            }

            if (ContainsAny(value,
                    "github", "gitlab", "bitbucket", "stackoverflow",
                    "developer", "coding"))
            {
                return "Development";
            }

            if (ContainsAny(value,
                    "amazon", "ebay", "aliexpress", "etsy",
                    "shopping", "shop"))
            {
                return "Shopping";
            }

            if (ContainsAny(value,
                    "openai", "chatgpt", "claude", "gemini",
                    "copilot", "artificial-intelligence"))
            {
                return "AI Services";
            }

            if (ContainsAny(value,
                    "gmail", "outlook", "protonmail", "yahoo-mail",
                    "email", "mail"))
            {
                return "Email";
            }

            if (ContainsAny(value,
                    "adult", "porn", "xxx"))
            {
                return "Adult Content";
            }

            return "Other";
        }

        private static bool ContainsAny(
            string value,
            params string[] terms)
        {
            return terms.Any(term =>
                value.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatBlockedServiceName(
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return string.Empty;
            }

            string text =
                id.Replace('_', ' ')
                  .Replace('-', ' ');

            return
                System.Globalization.CultureInfo
                    .InvariantCulture
                    .TextInfo
                    .ToTitleCase(
                        text.ToLowerInvariant());
        }

        private static string GetString(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
        private static string[] GetStringArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
            return array.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
        }

        //
        // AdGuard Statistics
        //

        public async Task<AdGuardStatistics>
            GetAdGuardStatisticsAsync()
        {
            AdGuardStatistics stats =
                CreateUnavailableStatistics();

            try
            {
                string token =
                    await GetAdminTokenAsync();

                AdGuardStatsResponse firstAttempt =
                    await RequestAdGuardStatisticsAsync(
                        token);

                if (firstAttempt.RequiresNewToken)
                {
                    Debug.WriteLine(
                        "The GL.iNet Admin-Token was rejected. " +
                        "Obtaining a new token and retrying.");

                    InvalidateAdminToken();

                    token =
                        await GetAdminTokenAsync();

                    AdGuardStatsResponse secondAttempt =
                        await RequestAdGuardStatisticsAsync(
                            token);

                    if (!secondAttempt.IsSuccess)
                    {
                        LogFailedAdGuardResponse(
                            secondAttempt);

                        return stats;
                    }

                    return ParseAdGuardStatistics(
                        secondAttempt.Content);
                }

                if (!firstAttempt.IsSuccess)
                {
                    LogFailedAdGuardResponse(
                        firstAttempt);

                    return stats;
                }

                return ParseAdGuardStatistics(
                    firstAttempt.Content);
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine(
                    "The AdGuard statistics request timed out.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(
                    "AdGuard HTTP error: " +
                    ex.Message);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(
                    "AdGuard JSON error: " +
                    ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "AdGuard statistics error: " +
                    ex);
            }

            return stats;
        }

        //
        // AdGuard Clients
        //

        public async Task<List<ClientInfo>>
            GetAdGuardClientsAsync()
        {
            try
            {
                string token =
                    await GetAdminTokenAsync();

                AdGuardClientsResponse clientsResponse =
                    await RequestAdGuardClientsAsync(
                        token);

                if (clientsResponse.RequiresNewToken)
                {
                    InvalidateAdminToken();

                    token =
                        await GetAdminTokenAsync();

                    clientsResponse =
                        await RequestAdGuardClientsAsync(
                            token);
                }

                if (!clientsResponse.IsSuccess)
                {
                    LogFailedClientsResponse(
                        clientsResponse);

                    return new List<ClientInfo>();
                }

                List<ClientInfo> clients =
                    ParseAdGuardClients(
                        clientsResponse.Content);

                AdGuardQueryLogResponse queryLogResponse =
                    await RequestAdGuardQueryLogAsync(
                        token);

                if (queryLogResponse.RequiresNewToken)
                {
                    InvalidateAdminToken();

                    token =
                        await GetAdminTokenAsync();

                    queryLogResponse =
                        await RequestAdGuardQueryLogAsync(
                            token);
                }

                int matchedQueryLogEntries = 0;
                bool queryLogAvailable = false;

                if (queryLogResponse.IsSuccess)
                {
                    queryLogAvailable =
                        QueryLogResponseHasEntries(
                            queryLogResponse.Content);

                    matchedQueryLogEntries =
                        ApplyQueryLogStatistics(
                            clients,
                            queryLogResponse.Content);
                }
                else
                {
                    LogFailedQueryLogResponse(
                        queryLogResponse);
                }

                // An empty log can also mean logging is disabled.  Confirm
                // configuration so cards can explain unavailable fields.
                try
                {
                    JsonElement queryLogConfig =
                        await GetControlJsonAsync(
                            "querylog/config");

                    queryLogAvailable =
                        GetBoolean(
                            queryLogConfig,
                            "enabled");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "Unable to read query-log configuration: " +
                        ex.Message);
                }

                foreach (ClientInfo client in clients)
                {
                    client.QueryLogAvailable =
                        queryLogAvailable;
                }

                // The query log and statistics store are independent in
                // AdGuard Home.  A valid query-log response can be empty
                // while /control/stats still contains live per-client totals.
                // Always merge top_clients so the cards do not collapse back
                // to zero merely because query-log retrieval is unavailable.
                AdGuardStatsResponse statsResponse =
                    await RequestAdGuardStatisticsAsync(
                        token);

                if (statsResponse.RequiresNewToken)
                {
                    InvalidateAdminToken();
                    token =
                        await GetAdminTokenAsync();

                    statsResponse =
                        await RequestAdGuardStatisticsAsync(
                            token);
                }

                int matchedStatisticsClients = 0;

                if (statsResponse.IsSuccess)
                {
                    matchedStatisticsClients =
                        ApplyClientTotalsFromStatistics(
                            clients,
                            statsResponse.Content);
                }
                else
                {
                    LogFailedAdGuardResponse(
                        statsResponse);
                }

                Debug.WriteLine(
                    "Client activity merge complete. " +
                    $"Query-log matches: {matchedQueryLogEntries}; " +
                    $"statistics matches: {matchedStatisticsClients}.");

                return clients;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine(
                    "The AdGuard clients request timed out.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(
                    "AdGuard clients HTTP error: " +
                    ex.Message);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(
                    "AdGuard clients JSON error: " +
                    ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "AdGuard clients error: " +
                    ex);
            }

            return new List<ClientInfo>();
        }


        public async Task<string> GetClientDiagnosticsAsync()
        {
            var report =
                new System.Text.StringBuilder();

            report.AppendLine("AdGuardTray Client Diagnostics");
            report.AppendLine(
                "Generated: " +
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            report.AppendLine(
                "Router: " +
                _routerIp);
            report.AppendLine();

            try
            {
                string token =
                    await GetAdminTokenAsync();

                report.AppendLine("Authentication");
                report.AppendLine("--------------");
                report.AppendLine("Admin token received: Yes");
                report.AppendLine(
                    "Token length: " +
                    token.Length);
                report.AppendLine();

                AdGuardClientsResponse clientsResponse =
                    await RequestAdGuardClientsAsync(
                        token);

                report.AppendLine("Clients endpoint");
                report.AppendLine("----------------");
                report.AppendLine(
                    $"HTTP {(int)clientsResponse.StatusCode} " +
                    clientsResponse.StatusCode);

                if (clientsResponse.IsSuccess)
                {
                    List<ClientInfo> clients =
                        ParseAdGuardClients(
                            clientsResponse.Content);

                    report.AppendLine(
                        "Configured clients parsed: " +
                        clients.Count);

                    string sampleClients =
                        string.Join(
                            ", ",
                            clients
                                .Take(8)
                                .Select(client =>
                                    $"{client.Name} [{client.IpAddress}]"));

                    report.AppendLine(
                        "Sample identifiers: " +
                        (sampleClients.Length == 0
                            ? "(none)"
                            : sampleClients));
                }

                report.AppendLine();

                AdGuardQueryLogResponse queryLogResponse =
                    await RequestAdGuardQueryLogAsync(
                        token,
                        500);

                report.AppendLine("Query-log endpoint");
                report.AppendLine("------------------");
                report.AppendLine(
                    $"HTTP {(int)queryLogResponse.StatusCode} " +
                    queryLogResponse.StatusCode);

                AppendQueryLogDiagnosticSummary(
                    report,
                    queryLogResponse.Content);

                report.AppendLine();

                AdGuardStatsResponse statsResponse =
                    await RequestAdGuardStatisticsAsync(
                        token);

                report.AppendLine("Statistics endpoint");
                report.AppendLine("-------------------");
                report.AppendLine(
                    $"HTTP {(int)statsResponse.StatusCode} " +
                    statsResponse.StatusCode);

                AppendStatisticsDiagnosticSummary(
                    report,
                    statsResponse.Content);

                report.AppendLine();

                AdGuardControlResponse queryLogConfig =
                    await RequestAdGuardControlAsync(
                        HttpMethod.Get,
                        "querylog/config",
                        token);

                report.AppendLine("Query-log configuration");
                report.AppendLine("-----------------------");
                report.AppendLine(
                    $"HTTP {(int)queryLogConfig.StatusCode} " +
                    queryLogConfig.StatusCode);

                AppendConfigurationDiagnosticSummary(
                    report,
                    queryLogConfig.Content);

                report.AppendLine();
                report.AppendLine("Interpretation");
                report.AppendLine("--------------");
                report.AppendLine(
                    "Queries are merged from statistics/top_clients. " +
                    "Blocked and Last seen require matching query-log entries.");
                report.AppendLine(
                    "A disabled query log is safe to repair from this page; " +
                    "the existing retention and privacy settings are preserved.");
            }
            catch (Exception ex)
            {
                report.AppendLine();
                report.AppendLine("Diagnostics failed");
                report.AppendLine("------------------");
                report.AppendLine(ex.ToString());
            }

            return report.ToString();
        }

        private static void AppendQueryLogDiagnosticSummary(
            System.Text.StringBuilder report,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                report.AppendLine("Response body: empty");
                return;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root =
                    document.RootElement;

                int count =
                    root.TryGetProperty(
                        "data",
                        out JsonElement data) &&
                    data.ValueKind == JsonValueKind.Array
                        ? data.GetArrayLength()
                        : -1;

                report.AppendLine(
                    "Entries returned: " +
                    (count < 0
                        ? "data array missing"
                        : count));

                report.AppendLine(
                    "Oldest cursor: " +
                    GetStringProperty(
                        root,
                        "oldest",
                        "(missing)"));

                if (count > 0)
                {
                    string sample =
                        string.Join(
                            ", ",
                            data.EnumerateArray()
                                .Take(8)
                                .Select(entry =>
                                    GetClientStringProperty(
                                        entry,
                                        "client"))
                                .Where(value =>
                                    !string.IsNullOrWhiteSpace(value)));

                    report.AppendLine(
                        "Sample client values: " +
                        (sample.Length == 0
                            ? "(none)"
                            : sample));
                }
            }
            catch (JsonException ex)
            {
                report.AppendLine(
                    "Invalid JSON: " +
                    ex.Message);
            }
        }

        private static void AppendStatisticsDiagnosticSummary(
            System.Text.StringBuilder report,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                report.AppendLine("Response body: empty");
                return;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root =
                    document.RootElement;

                report.AppendLine(
                    "Total DNS queries: " +
                    GetIntegerProperty(
                        root,
                        "num_dns_queries",
                        -1));

                report.AppendLine(
                    "Blocked queries: " +
                    GetIntegerProperty(
                        root,
                        "num_blocked_filtering",
                        -1));

                int topClientCount =
                    root.TryGetProperty(
                        "top_clients",
                        out JsonElement topClients) &&
                    topClients.ValueKind == JsonValueKind.Array
                        ? topClients.GetArrayLength()
                        : -1;

                report.AppendLine(
                    "top_clients entries: " +
                    (topClientCount < 0
                        ? "missing"
                        : topClientCount));

                if (topClientCount > 0)
                {
                    var samples =
                        new List<string>();

                    foreach (JsonElement item in
                        topClients.EnumerateArray().Take(8))
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        foreach (JsonProperty property in
                            item.EnumerateObject())
                        {
                            samples.Add(
                                $"{property.Name}={property.Value}");
                        }
                    }

                    report.AppendLine(
                        "Sample top_clients: " +
                        string.Join(", ", samples));
                }
            }
            catch (JsonException ex)
            {
                report.AppendLine(
                    "Invalid JSON: " +
                    ex.Message);
            }
        }

        private static void AppendConfigurationDiagnosticSummary(
            System.Text.StringBuilder report,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                report.AppendLine("Response body: empty");
                return;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root =
                    document.RootElement;

                report.AppendLine(
                    "Enabled: " +
                    GetBoolean(
                        root,
                        "enabled"));

                report.AppendLine(
                    "Anonymise client IP: " +
                    GetBoolean(
                        root,
                        "anonymize_client_ip"));

                report.AppendLine(
                    "Retention interval: " +
                    GetDouble(
                        root,
                        "interval",
                        -1));
            }
            catch (JsonException ex)
            {
                report.AppendLine(
                    "Invalid JSON: " +
                    ex.Message);
            }
        }

        private static int GetIntegerProperty(
            JsonElement root,
            string name,
            int fallback)
        {
            return root.TryGetProperty(
                       name,
                       out JsonElement value) &&
                   TryGetInteger(
                       value,
                       out int result)
                ? result
                : fallback;
        }

        //
        // AdGuard Query Log
        //

        public async Task<List<QueryLogEntry>>
            GetQueryLogAsync()
        {
            try
            {
                string token =
                    await GetAdminTokenAsync();

                AdGuardQueryLogResponse response =
                    await RequestAdGuardQueryLogAsync(
                        token,
                        500);

                if (response.RequiresNewToken)
                {
                    InvalidateAdminToken();

                    token =
                        await GetAdminTokenAsync();

                    response =
                        await RequestAdGuardQueryLogAsync(
                            token,
                            500);
                }

                if (!response.IsSuccess)
                {
                    LogFailedQueryLogResponse(
                        response);

                    return new List<QueryLogEntry>();
                }

                return ParseAdGuardQueryLog(
                    response.Content);
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine(
                    "The AdGuard query-log request timed out.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(
                    "AdGuard query-log HTTP error: " +
                    ex.Message);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(
                    "AdGuard query-log JSON error: " +
                    ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "AdGuard query-log error: " +
                    ex);
            }

            return new List<QueryLogEntry>();
        }

        private async Task<AdGuardClientsResponse>
            RequestAdGuardClientsAsync(
                string token)
        {
            var cookieContainer =
                new CookieContainer();

            var adGuardBaseUri =
                new Uri(
                    $"http://{_routerIp}:3000");

            cookieContainer.Add(
                adGuardBaseUri,
                new Cookie(
                    "Admin-Token",
                    token,
                    "/"));

            using var handler =
                new HttpClientHandler
                {
                    CookieContainer =
                        cookieContainer,

                    UseCookies =
                        true,

                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout =
                        TimeSpan.FromSeconds(10)
                };

            client.DefaultRequestHeaders
                .Accept
                .ParseAdd(
                    "application/json");

            string url =
                $"http://{_routerIp}:3000/control/clients";

            Debug.WriteLine(
                "Calling AdGuard clients: " +
                url);

            using HttpResponseMessage response =
                await client.GetAsync(
                    url);

            string content =
                await response.Content
                    .ReadAsStringAsync();

            Debug.WriteLine(
                "AdGuard clients status: " +
                $"{(int)response.StatusCode} " +
                response.StatusCode);

            return new AdGuardClientsResponse(
                response.StatusCode,
                content);
        }

        private async Task<AdGuardQueryLogResponse>
            RequestAdGuardQueryLogAsync(
                string token,
                int limit = 5000)
        {
            var cookieContainer =
                new CookieContainer();

            var adGuardBaseUri =
                new Uri(
                    $"http://{_routerIp}:3000");

            cookieContainer.Add(
                adGuardBaseUri,
                new Cookie(
                    "Admin-Token",
                    token,
                    "/"));

            using var handler =
                new HttpClientHandler
                {
                    CookieContainer = cookieContainer,
                    UseCookies = true,
                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

            client.DefaultRequestHeaders.Accept.ParseAdd(
                "application/json");

            client.DefaultRequestHeaders.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MustRevalidate = true
                };

            client.DefaultRequestHeaders.Pragma.ParseAdd(
                "no-cache");

            int safeLimit =
                Math.Clamp(limit, 1, 5000);

            long cacheBuster =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string futureCursor =
                Uri.EscapeDataString(
                    DateTimeOffset.UtcNow
                        .AddMinutes(1)
                        .ToString("O"));

            string[] urls =
            {
                $"http://{_routerIp}:3000/control/querylog" +
                $"?search=&response_status=&older_than=&limit={safeLimit}" +
                $"&_={cacheBuster}",

                $"http://{_routerIp}:3000/control/querylog" +
                $"?search=&response_status=&older_than={futureCursor}" +
                $"&limit={safeLimit}&_={cacheBuster + 1}",

                $"http://{_routerIp}:3000/control/querylog" +
                $"?limit={safeLimit}&_={cacheBuster + 2}"
            };

            AdGuardQueryLogResponse? lastResponse = null;

            foreach (string url in urls)
            {
                Debug.WriteLine(
                    "Calling AdGuard query log: " + url);

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url);

                request.Headers.CacheControl =
                    new System.Net.Http.Headers.CacheControlHeaderValue
                    {
                        NoCache = true,
                        NoStore = true,
                        MustRevalidate = true
                    };

                request.Headers.Pragma.ParseAdd(
                    "no-cache");

                using HttpResponseMessage response =
                    await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead);

                string content =
                    await response.Content.ReadAsStringAsync();

                lastResponse =
                    new AdGuardQueryLogResponse(
                        response.StatusCode,
                        content);

                Debug.WriteLine(
                    "AdGuard query log status: " +
                    $"{(int)response.StatusCode} {response.StatusCode}");

                if (response.IsSuccessStatusCode &&
                    QueryLogResponseHasEntries(content))
                {
                    return lastResponse;
                }
            }

            return lastResponse ??
                new AdGuardQueryLogResponse(
                    HttpStatusCode.ServiceUnavailable,
                    string.Empty);
        }

        private static bool QueryLogResponseHasEntries(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                return document.RootElement.TryGetProperty(
                           "data",
                           out JsonElement data) &&
                       data.ValueKind == JsonValueKind.Array &&
                       data.GetArrayLength() > 0;
            }
            catch
            {
                return false;
            }
        }

        private static List<QueryLogEntry>
            ParseAdGuardQueryLog(
                string json)
        {
            var entries =
                new List<QueryLogEntry>();

            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            JsonElement root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "data",
                    out JsonElement data) ||
                data.ValueKind !=
                    JsonValueKind.Array)
            {
                Debug.WriteLine(
                    "AdGuard query log did not contain " +
                    "a data array.");

                return entries;
            }

            foreach (JsonElement item
                     in data.EnumerateArray())
            {
                string timeText =
                    GetClientStringProperty(
                        item,
                        "time");

                string displayTime =
                    timeText;

                if (DateTimeOffset.TryParse(
                        timeText,
                        out DateTimeOffset timestamp))
                {
                    displayTime =
                        timestamp
                            .ToLocalTime()
                            .ToString(
                                "dd MMM yyyy HH:mm:ss");
                }

                string clientAddress =
                    GetClientStringProperty(
                        item,
                        "client");

                string clientName =
                    GetNestedStringProperty(
                        item,
                        "client_info",
                        "name");

                string client =
                    !string.IsNullOrWhiteSpace(clientName)
                        ? string.IsNullOrWhiteSpace(clientAddress)
                            ? clientName
                            : $"{clientName} ({clientAddress})"
                        : string.IsNullOrWhiteSpace(clientAddress)
                            ? "-"
                            : clientAddress;

                string domain =
                    GetQueryDomain(
                        item);

                string reason =
                    GetClientStringProperty(
                        item,
                        "reason");

                entries.Add(
                    new QueryLogEntry
                    {
                        Time =
                            string.IsNullOrWhiteSpace(
                                displayTime)
                                ? "-"
                                : displayTime,

                        Client =
                            client,

                        ClientAddress =
                            clientAddress,

                        ClientName =
                            clientName,

                        Domain =
                            string.IsNullOrWhiteSpace(
                                domain)
                                ? "-"
                                : domain,

                        IsBlocked =
                            IsBlockedQueryReason(
                                reason)
                    });
            }

            Debug.WriteLine(
                $"AdGuard query-log entries loaded: " +
                entries.Count);

            return entries;
        }

        private static string GetQueryDomain(
            JsonElement entry)
        {
            if (!entry.TryGetProperty(
                    "question",
                    out JsonElement question) ||
                question.ValueKind !=
                    JsonValueKind.Object)
            {
                return string.Empty;
            }

            return GetClientStringProperty(
                question,
                "name");
        }

        private static int ApplyQueryLogStatistics(
            List<ClientInfo> clients,
            string json)
        {
            var clientsByAddress =
                new Dictionary<string, ClientInfo>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ClientInfo client in clients)
            {
                if (!string.IsNullOrWhiteSpace(
                        client.IpAddress) &&
                    client.IpAddress != "-")
                {
                    clientsByAddress[
                        client.IpAddress.Trim()] =
                        client;
                }
            }

            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            JsonElement root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "data",
                    out JsonElement entries) ||
                entries.ValueKind !=
                    JsonValueKind.Array)
            {
                Debug.WriteLine(
                    "AdGuard query log did not contain " +
                    "a data array.");

                return 0;
            }

            int matchedEntries = 0;

            var mostRecentByClient =
                new Dictionary<string, DateTimeOffset>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement entry
                     in entries.EnumerateArray())
            {
                string clientAddress =
                    GetClientStringProperty(
                        entry,
                        "client");

                if (string.IsNullOrWhiteSpace(
                        clientAddress) ||
                    !clientsByAddress.TryGetValue(
                        clientAddress,
                        out ClientInfo? client))
                {
                    continue;
                }

                matchedEntries++;
                client.TotalQueries++;

                string reason =
                    GetClientStringProperty(
                        entry,
                        "reason");

                if (IsBlockedQueryReason(
                        reason))
                {
                    client.BlockedQueries++;
                }

                string timeText =
                    GetClientStringProperty(
                        entry,
                        "time");

                if (DateTimeOffset.TryParse(
                        timeText,
                        out DateTimeOffset timestamp))
                {
                    if (!mostRecentByClient.TryGetValue(
                            clientAddress,
                            out DateTimeOffset current) ||
                        timestamp > current)
                    {
                        mostRecentByClient[
                            clientAddress] =
                            timestamp;
                    }
                }
            }

            foreach (KeyValuePair<string, DateTimeOffset> item
                     in mostRecentByClient)
            {
                if (clientsByAddress.TryGetValue(
                        item.Key,
                        out ClientInfo? client))
                {
                    client.LastSeen =
                        item.Value
                            .ToLocalTime()
                            .ToString(
                                "dd MMM yyyy HH:mm:ss");
                }
            }

            Debug.WriteLine(
                "Applied query-log statistics to " +
                $"{mostRecentByClient.Count} clients " +
                $"from {matchedEntries} matching entries.");

            return matchedEntries;
        }

        private static int ApplyClientTotalsFromStatistics(
            List<ClientInfo> clients,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return 0;
            }

            var clientsByIdentifier =
                new Dictionary<string, ClientInfo>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ClientInfo client in clients)
            {
                AddClientIdentifier(
                    clientsByIdentifier,
                    client.IpAddress,
                    client);

                AddClientIdentifier(
                    clientsByIdentifier,
                    client.Name,
                    client);
            }

            using JsonDocument document =
                JsonDocument.Parse(json);

            JsonElement root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "top_clients",
                    out JsonElement topClients) ||
                topClients.ValueKind != JsonValueKind.Array)
            {
                Debug.WriteLine(
                    "AdGuard statistics did not contain top_clients.");

                return 0;
            }

            int matchedClients = 0;

            foreach (JsonElement item in topClients.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty property in item.EnumerateObject())
                {
                    if (!TryGetInteger(
                            property.Value,
                            out int count))
                    {
                        continue;
                    }

                    string identifier =
                        property.Name.Trim();

                    if (!clientsByIdentifier.TryGetValue(
                            identifier,
                            out ClientInfo? client))
                    {
                        continue;
                    }

                    // Query-log counts describe the returned page, whereas
                    // top_clients describes the configured statistics window.
                    // Keep whichever source provides the larger real total.
                    client.TotalQueries =
                        Math.Max(
                            client.TotalQueries,
                            count);

                    matchedClients++;
                    break;
                }
            }

            Debug.WriteLine(
                "Applied statistics totals to " +
                $"{matchedClients} clients.");

            return matchedClients;
        }

        private static void AddClientIdentifier(
            Dictionary<string, ClientInfo> lookup,
            string? identifier,
            ClientInfo client)
        {
            if (string.IsNullOrWhiteSpace(identifier) ||
                identifier == "-")
            {
                return;
            }

            lookup[identifier.Trim()] =
                client;
        }

        private static bool IsBlockedQueryReason(
            string reason)
        {
            if (string.IsNullOrWhiteSpace(
                    reason))
            {
                return false;
            }

            bool filteredBlock =
                reason.StartsWith(
                    "Filtered",
                    StringComparison.OrdinalIgnoreCase) &&
                !reason.Contains(
                    "WhiteList",
                    StringComparison.OrdinalIgnoreCase);

            return filteredBlock ||
                   reason.Equals(
                       "SafeBrowsing",
                       StringComparison.OrdinalIgnoreCase) ||
                   reason.Equals(
                       "Parental",
                       StringComparison.OrdinalIgnoreCase) ||
                   reason.Equals(
                       "SafeSearch",
                       StringComparison.OrdinalIgnoreCase) ||
                   reason.Equals(
                       "BlockedService",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void LogFailedQueryLogResponse(
            AdGuardQueryLogResponse response)
        {
            Debug.WriteLine(
                "AdGuard query-log request failed with status " +
                $"{(int)response.StatusCode} " +
                response.StatusCode +
                ".");

            if (!string.IsNullOrWhiteSpace(
                    response.Content))
            {
                Debug.WriteLine(
                    "AdGuard query-log response: " +
                    response.Content);
            }
        }

        private static List<ClientInfo>
            ParseAdGuardClients(
                string json)
        {
            var clients =
                new List<ClientInfo>();

            var knownIdentifiers =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            JsonElement root =
                document.RootElement;

            ParseConfiguredClients(
                root,
                clients,
                knownIdentifiers);

            ParseAutomaticClients(
                root,
                clients,
                knownIdentifiers);

            clients.Sort(
                (left, right) =>
                    string.Compare(
                        left.Name,
                        right.Name,
                        StringComparison.OrdinalIgnoreCase));

            Debug.WriteLine(
                $"AdGuard clients loaded: {clients.Count}");

            return clients;
        }

        private static void ParseConfiguredClients(
            JsonElement root,
            List<ClientInfo> clients,
            HashSet<string> knownIdentifiers)
        {
            if (!root.TryGetProperty(
                    "clients",
                    out JsonElement configuredClients) ||
                configuredClients.ValueKind !=
                    JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement configuredClient
                     in configuredClients.EnumerateArray())
            {
                string name =
                    GetClientStringProperty(
                        configuredClient,
                        "name");

                if (!configuredClient.TryGetProperty(
                        "ids",
                        out JsonElement identifiers) ||
                    identifiers.ValueKind !=
                        JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement identifierElement
                         in identifiers.EnumerateArray())
                {
                    if (identifierElement.ValueKind !=
                        JsonValueKind.String)
                    {
                        continue;
                    }

                    string identifier =
                        identifierElement
                            .GetString()?
                            .Trim() ??
                        string.Empty;

                    AddClient(
                        clients,
                        knownIdentifiers,
                        name,
                        identifier);
                }
            }
        }

        private static void ParseAutomaticClients(
            JsonElement root,
            List<ClientInfo> clients,
            HashSet<string> knownIdentifiers)
        {
            if (!root.TryGetProperty(
                    "auto_clients",
                    out JsonElement automaticClients) ||
                automaticClients.ValueKind !=
                    JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement automaticClient
                     in automaticClients.EnumerateArray())
            {
                string name =
                    GetClientStringProperty(
                        automaticClient,
                        "name");

                string ipAddress =
                    GetClientStringProperty(
                        automaticClient,
                        "ip");

                AddClient(
                    clients,
                    knownIdentifiers,
                    name,
                    ipAddress);
            }
        }

        private static void AddClient(
            List<ClientInfo> clients,
            HashSet<string> knownIdentifiers,
            string name,
            string identifier)
        {
            if (string.IsNullOrWhiteSpace(
                    identifier))
            {
                return;
            }

            string normalisedIdentifier =
                identifier.Trim();

            if (!knownIdentifiers.Add(
                    normalisedIdentifier))
            {
                return;
            }

            string displayName =
                string.IsNullOrWhiteSpace(name)
                    ? normalisedIdentifier
                    : name.Trim();

            string ipAddress =
                "-";

            string macAddress =
                "-";

            if (IPAddress.TryParse(
                    normalisedIdentifier,
                    out _))
            {
                ipAddress =
                    normalisedIdentifier;
            }
            else if (LooksLikeMacAddress(
                         normalisedIdentifier))
            {
                macAddress =
                    normalisedIdentifier;
            }
            else
            {
                ipAddress =
                    normalisedIdentifier;
            }

            // Configured AdGuard Home clients can expose an IP address
            // and a MAC address as separate identifiers with the same name.
            // Merge those identifiers into one card instead of creating two
            // incomplete client records.
            if (!string.IsNullOrWhiteSpace(name))
            {
                ClientInfo? existingClient =
                    clients.FirstOrDefault(
                        client =>
                            string.Equals(
                                client.Name,
                                displayName,
                                StringComparison.OrdinalIgnoreCase));

                if (existingClient is not null)
                {
                    if (ipAddress != "-" &&
                        existingClient.IpAddress == "-")
                    {
                        existingClient.IpAddress = ipAddress;
                    }

                    if (macAddress != "-" &&
                        existingClient.MacAddress == "-")
                    {
                        existingClient.MacAddress = macAddress;
                    }

                    return;
                }
            }

            clients.Add(
                new ClientInfo
                {
                    Name =
                        displayName,

                    IpAddress =
                        ipAddress,

                    MacAddress =
                        macAddress,

                    TotalQueries =
                        0,

                    BlockedQueries =
                        0,

                    LastSeen =
                        "-"
                });
        }

        private static string GetClientStringProperty(
            JsonElement element,
            string propertyName)
        {
            if (element.TryGetProperty(
                    propertyName,
                    out JsonElement property) &&
                property.ValueKind ==
                    JsonValueKind.String)
            {
                return property
                           .GetString()?
                           .Trim() ??
                       string.Empty;
            }

            return string.Empty;
        }

        private static string GetNestedStringProperty(
            JsonElement element,
            string objectPropertyName,
            string stringPropertyName)
        {
            if (!element.TryGetProperty(
                    objectPropertyName,
                    out JsonElement nestedObject) ||
                nestedObject.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return GetClientStringProperty(
                nestedObject,
                stringPropertyName);
        }

        private static bool LooksLikeMacAddress(
            string value)
        {
            string compactValue =
                value
                    .Replace(
                        ":",
                        string.Empty,
                        StringComparison.Ordinal)
                    .Replace(
                        "-",
                        string.Empty,
                        StringComparison.Ordinal);

            if (compactValue.Length != 12)
            {
                return false;
            }

            foreach (char character
                     in compactValue)
            {
                bool isHexadecimal =
                    character >= '0' &&
                    character <= '9' ||
                    character >= 'a' &&
                    character <= 'f' ||
                    character >= 'A' &&
                    character <= 'F';

                if (!isHexadecimal)
                {
                    return false;
                }
            }

            return true;
        }

        private static void LogFailedClientsResponse(
            AdGuardClientsResponse response)
        {
            if (response.RequiresNewToken)
            {
                Debug.WriteLine(
                    "The GL.iNet Admin-Token is missing, " +
                    "invalid or expired.");
            }

            Debug.WriteLine(
                "AdGuard clients request failed with status " +
                $"{(int)response.StatusCode} " +
                response.StatusCode +
                ".");

            if (!string.IsNullOrWhiteSpace(
                    response.Content))
            {
                Debug.WriteLine(
                    "AdGuard clients response: " +
                    response.Content);
            }
        }

        private async Task<string>
            GetAdminTokenAsync()
        {
            if (!string.IsNullOrWhiteSpace(
                    _adminToken))
            {
                return _adminToken;
            }

            await _tokenLock.WaitAsync();

            try
            {
                if (!string.IsNullOrWhiteSpace(
                        _adminToken))
                {
                    return _adminToken;
                }

                Debug.WriteLine(
                    "No cached GL.iNet Admin-Token is available. " +
                    "Logging in automatically.");

                string token =
                    await _sessionService
                        .GetAdminTokenAsync(
                            CancellationToken.None);

                if (string.IsNullOrWhiteSpace(
                        token))
                {
                    throw new InvalidOperationException(
                        "GL.iNet login succeeded but no " +
                        "Admin-Token was returned.");
                }

                _adminToken =
                    token;

                Debug.WriteLine(
                    "GL.iNet Admin-Token obtained successfully.");

                return token;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private void InvalidateAdminToken()
        {
            _adminToken =
                null;
        }

        private async Task<AdGuardStatsResponse>
            RequestAdGuardStatisticsAsync(
                string token)
        {
            var cookieContainer =
                new CookieContainer();

            var adGuardBaseUri =
                new Uri(
                    $"http://{_routerIp}:3000");

            cookieContainer.Add(
                adGuardBaseUri,
                new Cookie(
                    "Admin-Token",
                    token,
                    "/"));

            using var handler =
                new HttpClientHandler
                {
                    CookieContainer =
                        cookieContainer,

                    UseCookies =
                        true,

                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout =
                        TimeSpan.FromSeconds(10)
                };

            client.DefaultRequestHeaders
                .Accept
                .ParseAdd(
                    "application/json");

            string url =
                $"http://{_routerIp}:3000/control/stats";

            Debug.WriteLine(
                "Calling AdGuard stats: " +
                url);

            using HttpResponseMessage response =
                await client.GetAsync(
                    url);

            string content =
                await response.Content
                    .ReadAsStringAsync();

            Debug.WriteLine(
                "AdGuard status: " +
                $"{(int)response.StatusCode} " +
                response.StatusCode);

            return new AdGuardStatsResponse(
                response.StatusCode,
                content);
        }

        private static AdGuardStatistics
            ParseAdGuardStatistics(
                string json)
        {
            AdGuardStatistics stats =
                CreateUnavailableStatistics();

            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            JsonElement root =
                document.RootElement;

            if (root.TryGetProperty(
                    "num_dns_queries",
                    out JsonElement queries) &&
                queries.TryGetInt32(
                    out int totalQueries))
            {
                stats.TotalQueries =
                    totalQueries;
            }

            if (root.TryGetProperty(
                    "num_blocked_filtering",
                    out JsonElement blocked) &&
                blocked.TryGetInt32(
                    out int blockedQueries))
            {
                stats.BlockedQueries =
                    blockedQueries;
            }

            stats.QueryHistory =
                ParseQueryHistory(
                    root);

            stats.TopClients =
                ParseRankedItems(
                    root,
                    "top_clients");

            stats.TopQueriedDomains =
                ParseRankedItems(
                    root,
                    "top_queried_domains");

            stats.TopBlockedDomains =
                ParseRankedItems(
                    root,
                    "top_blocked_domains");

            Debug.WriteLine(
                $"Queries: {stats.TotalQueries}");

            Debug.WriteLine(
                $"Blocked: {stats.BlockedQueries}");

            Debug.WriteLine(
                $"Top clients: {stats.TopClients.Count}");

            Debug.WriteLine(
                $"Top requested: {stats.TopQueriedDomains.Count}");

            Debug.WriteLine(
                $"Top blocked: {stats.TopBlockedDomains.Count}");

            return stats;
        }

        private static List<AdGuardRankedItem>
            ParseRankedItems(
                JsonElement root,
                string propertyName)
        {
            var result =
                new List<AdGuardRankedItem>();

            if (!root.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                return result;
            }

            if (value.ValueKind ==
                JsonValueKind.Array)
            {
                foreach (JsonElement item in
                    value.EnumerateArray())
                {
                    if (item.ValueKind ==
                        JsonValueKind.Object)
                    {
                        // AdGuard Home normally returns one-property objects,
                        // for example {"example.com": 42}.
                        foreach (JsonProperty property in
                            item.EnumerateObject())
                        {
                            if (TryGetInteger(
                                    property.Value,
                                    out int propertyCount))
                            {
                                result.Add(
                                    new AdGuardRankedItem
                                    {
                                        Name = property.Name,
                                        Count = propertyCount
                                    });
                            }
                        }

                        // Also accept named object schemas used by forks.
                        string name =
                            GetStringProperty(
                                item,
                                "name",
                                string.Empty);

                        if (name.Length == 0)
                        {
                            name =
                                GetStringProperty(
                                    item,
                                    "domain",
                                    string.Empty);
                        }

                        if (name.Length == 0)
                        {
                            name =
                                GetStringProperty(
                                    item,
                                    "client",
                                    string.Empty);
                        }

                        if (name.Length > 0 &&
                            TryGetNamedInteger(
                                item,
                                out int namedCount))
                        {
                            result.Add(
                                new AdGuardRankedItem
                                {
                                    Name = name,
                                    Count = namedCount
                                });
                        }
                    }
                }
            }
            else if (value.ValueKind ==
                     JsonValueKind.Object)
            {
                foreach (JsonProperty property in
                    value.EnumerateObject())
                {
                    if (TryGetInteger(
                            property.Value,
                            out int mappedCount))
                    {
                        result.Add(
                            new AdGuardRankedItem
                            {
                                Name = property.Name,
                                Count = mappedCount
                            });
                    }
                }
            }

            return result
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(
                    item => item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    new AdGuardRankedItem
                    {
                        Name = group.Key,
                        Count = group.Sum(item => item.Count)
                    })
                .OrderByDescending(item => item.Count)
                .Take(10)
                .ToList();
        }

        private static bool TryGetNamedInteger(
            JsonElement item,
            out int value)
        {
            foreach (string propertyName in
                new[]
                {
                    "count",
                    "queries",
                    "value",
                    "num"
                })
            {
                if (item.TryGetProperty(
                        propertyName,
                        out JsonElement property) &&
                    TryGetInteger(
                        property,
                        out value))
                {
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static bool TryGetInteger(
            JsonElement value,
            out int result)
        {
            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetInt32(
                    out result))
            {
                return true;
            }

            if (value.ValueKind ==
                    JsonValueKind.String &&
                int.TryParse(
                    value.GetString(),
                    out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private static List<AdGuardTimePoint>
            ParseQueryHistory(
                JsonElement root)
        {
            var history =
                new List<AdGuardTimePoint>();

            if (!root.TryGetProperty(
                    "dns_queries",
                    out JsonElement queryArray) ||
                queryArray.ValueKind !=
                    JsonValueKind.Array)
            {
                Debug.WriteLine(
                    "AdGuard statistics did not contain " +
                    "a dns_queries array.");

                return history;
            }

            root.TryGetProperty(
                "blocked_filtering",
                out JsonElement blockedArray);

            string timeUnits =
                GetStringProperty(
                    root,
                    "time_units",
                    "hours");

            int pointCount =
                queryArray.GetArrayLength();

            DateTime now =
                DateTime.Now;

            for (int index = 0;
                 index < pointCount;
                 index++)
            {
                int queryCount =
                    GetArrayInteger(
                        queryArray,
                        index);

                int blockedCount =
                    0;

                if (blockedArray.ValueKind ==
                        JsonValueKind.Array &&
                    index <
                        blockedArray.GetArrayLength())
                {
                    blockedCount =
                        GetArrayInteger(
                            blockedArray,
                            index);
                }

                int intervalsAgo =
                    pointCount -
                    index -
                    1;

                DateTime timestamp =
                    SubtractTimeInterval(
                        now,
                        timeUnits,
                        intervalsAgo);

                history.Add(
                    new AdGuardTimePoint
                    {
                        Timestamp =
                            timestamp,

                        Queries =
                            queryCount,

                        Blocked =
                            blockedCount
                    });
            }

            return history;
        }

        private static int GetArrayInteger(
            JsonElement array,
            int index)
        {
            JsonElement value =
                array[index];

            if (value.TryGetInt32(
                    out int integerValue))
            {
                return integerValue;
            }

            if (value.TryGetInt64(
                    out long longValue))
            {
                if (longValue > int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (longValue < int.MinValue)
                {
                    return int.MinValue;
                }

                return (int)longValue;
            }

            return 0;
        }

        private static string GetStringProperty(
            JsonElement root,
            string propertyName,
            string fallbackValue)
        {
            if (root.TryGetProperty(
                    propertyName,
                    out JsonElement property) &&
                property.ValueKind ==
                    JsonValueKind.String)
            {
                string? value =
                    property.GetString();

                if (!string.IsNullOrWhiteSpace(
                        value))
                {
                    return value;
                }
            }

            return fallbackValue;
        }

        private static DateTime SubtractTimeInterval(
            DateTime timestamp,
            string timeUnits,
            int intervalCount)
        {
            return timeUnits
                .ToLowerInvariant() switch
            {
                "seconds" =>
                    timestamp.AddSeconds(
                        -intervalCount),

                "minutes" =>
                    timestamp.AddMinutes(
                        -intervalCount),

                "days" =>
                    timestamp.AddDays(
                        -intervalCount),

                "months" =>
                    timestamp.AddMonths(
                        -intervalCount),

                _ =>
                    timestamp.AddHours(
                        -intervalCount)
            };
        }

        private static void LogFailedAdGuardResponse(
            AdGuardStatsResponse response)
        {
            if (response.RequiresNewToken)
            {
                Debug.WriteLine(
                    "The GL.iNet Admin-Token is missing, " +
                    "invalid or expired.");
            }

            Debug.WriteLine(
                "AdGuard request failed with status " +
                $"{(int)response.StatusCode} " +
                response.StatusCode +
                ".");

            if (!string.IsNullOrWhiteSpace(
                    response.Content))
            {
                Debug.WriteLine(
                    "AdGuard response: " +
                    response.Content);
            }
        }

        private static AdGuardStatistics
            CreateUnavailableStatistics()
        {
            return new AdGuardStatistics
            {
                TotalQueries =
                    -1,

                BlockedQueries =
                    -1,

                QueryHistory =
                    new List<AdGuardTimePoint>()
            };
        }

        private static string NormaliseRouterHost(
            string routerIp)
        {
            string value =
                routerIp.Trim();

            if (Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out Uri? uri) &&
                !string.IsNullOrWhiteSpace(
                    uri.Host))
            {
                return uri.Host;
            }

            value =
                value
                    .Replace(
                        "https://",
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    .Replace(
                        "http://",
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('/');

            int slashIndex =
                value.IndexOf('/');

            if (slashIndex >= 0)
            {
                value =
                    value[..slashIndex];
            }

            int colonIndex =
                value.IndexOf(':');

            if (colonIndex >= 0)
            {
                value =
                    value[..colonIndex];
            }

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                throw new ArgumentException(
                    "The router address is invalid.",
                    nameof(routerIp));
            }

            return value;
        }

        //
        // Client diagnostics
        //

        public async Task<string> PingClientAsync(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out IPAddress? parsedAddress))
            {
                throw new ArgumentException(
                    "The client IP address is invalid.",
                    nameof(ipAddress));
            }

            string safeAddress = parsedAddress.ToString();

            string output = await _ssh.RunCommandAsync(
                $"ping -c 3 -W 2 {safeAddress} 2>&1");

            if (output.Contains("0% packet loss", StringComparison.OrdinalIgnoreCase))
            {
                string latency = "reachable";
                int marker = output.IndexOf("min/avg/max", StringComparison.OrdinalIgnoreCase);

                if (marker >= 0)
                {
                    int equals = output.IndexOf('=', marker);
                    int ms = output.IndexOf(" ms", equals, StringComparison.OrdinalIgnoreCase);

                    if (equals >= 0 && ms > equals)
                    {
                        string[] values = output[(equals + 1)..ms]
                            .Trim()
                            .Split('/');

                        if (values.Length >= 2)
                        {
                            latency = $"{values[1]} ms average";
                        }
                    }
                }

                return $"{safeAddress} is online ({latency}).";
            }

            return $"{safeAddress} did not respond to ping.";
        }

        public async Task<string> WakeClientAsync(string macAddress)
        {
            string normalized = (macAddress ?? string.Empty)
                .Trim()
                .Replace('-', ':')
                .ToUpperInvariant();

            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    normalized,
                    "^([0-9A-F]{2}:){5}[0-9A-F]{2}$"))
            {
                throw new ArgumentException(
                    "The client MAC address is invalid.",
                    nameof(macAddress));
            }

            string command =
                "if command -v etherwake >/dev/null 2>&1; then " +
                $"etherwake -i br-lan {normalized} 2>&1; " +
                "elif command -v wol >/dev/null 2>&1; then " +
                $"wol {normalized} 2>&1; " +
                "else echo '__WOL_TOOL_MISSING__'; fi";

            string output = await _ssh.RunCommandAsync(command);

            if (output.Contains(
                    "__WOL_TOOL_MISSING__",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Wake-on-LAN is not available on this router. Install etherwake to enable it.";
            }

            if (output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                return "The router could not send the Wake-on-LAN packet: " +
                       output.Trim();
            }

            return $"Wake-on-LAN packet sent to {normalized}.";
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
        // Router diagnostic tools
        //

        public Task<string> PingAsync(string target)
        {
            string safeTarget = ValidateDiagnosticTarget(target);

            return _ssh.RunCommandAsync(
                $"ping -c 4 -W 2 {safeTarget}");
        }

        public Task<string> TracerouteAsync(string target)
        {
            string safeTarget = ValidateDiagnosticTarget(target);

            return _ssh.RunCommandAsync(
                $"traceroute -m 12 -w 2 {safeTarget}");
        }

        public Task<string> DnsLookupAsync(string target)
        {
            string safeTarget = ValidateDiagnosticTarget(target);

            return _ssh.RunCommandAsync(
                $"nslookup {safeTarget}");
        }

        private static string ValidateDiagnosticTarget(string target)
        {
            string value = (target ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Enter a hostname or IP address.",
                    nameof(target));
            }

            if (value.Length > 253 ||
                value.Any(character =>
                    !(char.IsLetterOrDigit(character) ||
                      character == '.' ||
                      character == '-' ||
                      character == ':' ||
                      character == '_')))
            {
                throw new ArgumentException(
                    "The diagnostic target contains unsupported characters.",
                    nameof(target));
            }

            return value;
        }

        //
        // Reboot
        //

        public Task RebootRouterAsync()
        {
            return _ssh.RunCommandAsync(
                "reboot");
        }

        private sealed class AdGuardControlResponse
        {
            public AdGuardControlResponse(
                HttpStatusCode statusCode,
                string content)
            {
                StatusCode =
                    statusCode;

                Content =
                    content;
            }

            public HttpStatusCode StatusCode
            {
                get;
            }

            public string Content
            {
                get;
            }

            public bool IsSuccess =>
                (int)StatusCode >= 200 &&
                (int)StatusCode <= 299;

            public bool RequiresNewToken =>
                StatusCode ==
                    HttpStatusCode.Unauthorized ||
                StatusCode ==
                    HttpStatusCode.Forbidden;
        }

        private sealed class AdGuardClientsResponse
        {
            public AdGuardClientsResponse(
                HttpStatusCode statusCode,
                string content)
            {
                StatusCode =
                    statusCode;

                Content =
                    content;
            }

            public HttpStatusCode StatusCode
            {
                get;
            }

            public string Content
            {
                get;
            }

            public bool IsSuccess =>
                (int)StatusCode >= 200 &&
                (int)StatusCode <= 299;

            public bool RequiresNewToken =>
                StatusCode ==
                    HttpStatusCode.Unauthorized ||
                StatusCode ==
                    HttpStatusCode.Forbidden;
        }

        private sealed class AdGuardQueryLogResponse
        {
            public AdGuardQueryLogResponse(
                HttpStatusCode statusCode,
                string content)
            {
                StatusCode =
                    statusCode;

                Content =
                    content;
            }

            public HttpStatusCode StatusCode
            {
                get;
            }

            public string Content
            {
                get;
            }

            public bool IsSuccess =>
                (int)StatusCode >= 200 &&
                (int)StatusCode <= 299;

            public bool RequiresNewToken =>
                StatusCode ==
                    HttpStatusCode.Unauthorized ||
                StatusCode ==
                    HttpStatusCode.Forbidden;
        }

        private sealed class AdGuardStatsResponse
        {
            public AdGuardStatsResponse(
                HttpStatusCode statusCode,
                string content)
            {
                StatusCode =
                    statusCode;

                Content =
                    content;
            }

            public HttpStatusCode StatusCode
            {
                get;
            }

            public string Content
            {
                get;
            }

            public bool IsSuccess =>
                (int)StatusCode >= 200 &&
                (int)StatusCode <= 299;

            public bool RequiresNewToken =>
                StatusCode ==
                    HttpStatusCode.Unauthorized ||
                StatusCode ==
                    HttpStatusCode.Forbidden;
        }
    }
}