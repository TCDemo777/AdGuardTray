using System.Net.Http;
using System.Text.Json;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public class AdGuardApiClient
{
    private readonly HttpClient _httpClient;

    public AdGuardApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StatusResponse?> GetStatusAsync()
    {
        var response = await _httpClient.GetAsync("/control/status");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<StatusResponse>(json);
    }
}