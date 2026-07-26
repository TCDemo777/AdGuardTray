using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
                        TimeSpan.FromSeconds(15)
                };

            client.DefaultRequestHeaders
                .Accept
                .ParseAdd(
                    "application/json");

            string url =
                $"http://{_routerIp}:3000/control/querylog" +
                "?limit=5000";

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