using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RouterPilot.Services;

public sealed class RouterService : IDisposable
{
    private readonly HttpClient _client;
    private readonly RouterEndpointProvider _endpoints;
    private bool _disposed;

    public RouterService(RouterEndpointProvider endpoints)
    {
        _endpoints = endpoints ??
            throw new ArgumentNullException(nameof(endpoints));

        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(
                endpoints.Options.RequestTimeoutSeconds)
        };
    }

    public async Task OpenCorrectPageAsync(
        CancellationToken cancellationToken = default)
    {
        Uri target = await IsAdGuardAvailableAsync(cancellationToken)
            .ConfigureAwait(false)
            ? _endpoints.AdGuardBaseUri
            : _endpoints.RouterBaseUri;

        OpenBrowser(target);
    }

    public async Task<bool> IsAdGuardAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync(
                _endpoints.AdGuardBaseUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> CheckRouterLoginAsync(
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "call",
            @params = new object[]
            {
                "",
                "system",
                "get_info",
                new { }
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _client.PostAsync(
                _endpoints.RouterRpcUri,
                content,
                cancellationToken)
                .ConfigureAwait(false);

            string result = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode &&
                   result.Contains(
                       "\"hostname\"",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.Dispose();
    }
}
