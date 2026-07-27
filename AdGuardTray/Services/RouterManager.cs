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
            JsonElement all = await GetControlJsonAsync("blocked_services/all");
            JsonElement configJson = await GetControlJsonAsync("blocked_services/get");
            var config = new AdGuardBlockedServicesConfig();
            if (configJson.TryGetProperty("schedule", out JsonElement schedule)) config.ScheduleJson = schedule.GetRawText();
            foreach (string id in GetStringArray(configJson, "ids")) config.EnabledIds.Add(id);

            var result = new List<BlockedServiceItem>();

            JsonElement array = default;
            if (all.ValueKind == JsonValueKind.Array)
            {
                array = all;
            }
            else if (all.ValueKind == JsonValueKind.Object)
            {
                // AdGuard Home versions have returned this catalogue under
                // both "blocked_services" and "services".
                if (!all.TryGetProperty("blocked_services", out array))
                {
                    all.TryGetProperty("services", out array);
                }
            }

            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in array.EnumerateArray())
                {
                    string id;
                    string name;

                    if (item.ValueKind == JsonValueKind.String)
                    {
                        id = item.GetString()?.Trim() ?? string.Empty;
                        name = FormatBlockedServiceName(id);
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        id = GetString(item, "id");
                        if (id.Length == 0) id = GetString(item, "service_id");

                        name = GetString(item, "name");
                        if (name.Length == 0) name = GetString(item, "display_name");
                        if (name.Length == 0) name = FormatBlockedServiceName(id);
                    }
                    else
                    {
                        continue;
                    }

                    if (id.Length == 0 || result.Any(service =>
                        service.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    result.Add(new BlockedServiceItem
                    {
                        Id = id,
                        Name = name,
                        IsBlocked = config.EnabledIds.Contains(id)
                    });
                }
            }

            return (result, config);
        }

        private static string FormatBlockedServiceName(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "Unknown service";

            string text = id.Replace('_', ' ').Replace('-', ' ').Trim();
            return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..]));
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

                if (queryLogResponse.IsSuccess)
                {
                    ApplyQueryLogStatistics(
                        clients,
                        queryLogResponse.Content);
                }
                else
                {
                    LogFailedQueryLogResponse(
                        queryLogResponse);
                }

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
                        TimeSpan.FromSeconds(15)
                };

            client.DefaultRequestHeaders
                .Accept
                .ParseAdd(
                    "application/json");

            int safeLimit =
                Math.Clamp(
                    limit,
                    1,
                    5000);

            string url =
                $"http://{_routerIp}:3000/control/querylog" +
                $"?limit={safeLimit}";

            Debug.WriteLine(
                "Calling AdGuard query log: " +
                url);

            using HttpResponseMessage response =
                await client.GetAsync(
                    url);

            string content =
                await response.Content
                    .ReadAsStringAsync();

            Debug.WriteLine(
                "AdGuard query log status: " +
                $"{(int)response.StatusCode} " +
                response.StatusCode);

            return new AdGuardQueryLogResponse(
                response.StatusCode,
                content);
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

                string client =
                    GetClientStringProperty(
                        item,
                        "client");

                if (string.IsNullOrWhiteSpace(
                        client))
                {
                    client =
                        "-";
                }

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

        private static void ApplyQueryLogStatistics(
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

                return;
            }

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
                $"{mostRecentByClient.Count} clients.");
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

            Debug.WriteLine(
                $"Queries: {stats.TotalQueries}");

            Debug.WriteLine(
                $"Blocked: {stats.BlockedQueries}");

            Debug.WriteLine(
                $"History points: {stats.QueryHistory.Count}");

            return stats;
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