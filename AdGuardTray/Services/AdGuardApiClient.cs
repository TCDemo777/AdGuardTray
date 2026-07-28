using System.Text.Json;
using AdGuardTray.Models;
using System.Net.Http;

namespace AdGuardTray.Services;

public class AdGuardApiClient
{
    private readonly HttpClient _httpClient;

    public AdGuardApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StatsResponse?> GetStatsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/control/stats");

            var json = await response.Content.ReadAsStringAsync();

            Console.WriteLine("===== AdGuard Stats Test =====");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine(json);
            Console.WriteLine("==============================");

            response.EnsureSuccessStatusCode();

            return JsonSerializer.Deserialize<StatsResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine("AdGuard API Error:");
            Console.WriteLine(ex.Message);

            return null;
        }
    }
}