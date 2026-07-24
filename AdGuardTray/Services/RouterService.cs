using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdGuardTray.Services
{
    public class RouterService
    {
        private readonly HttpClient _client = new HttpClient();

        private const string RouterUrl = "http://192.168.1.1/";
        private const string AdGuardUrl = "http://192.168.1.1:3000/";



        public async Task OpenCorrectPageAsync()
        {
            try
            {
                if (await IsAdGuardAvailableAsync())
                {
                    OpenBrowser(AdGuardUrl);
                }
                else
                {
                    OpenBrowser(RouterUrl);
                }
            }
            catch
            {
                OpenBrowser(RouterUrl);
            }
        }



        public async Task<bool> IsAdGuardAvailableAsync()
        {
            try
            {
                using var response = await _client.GetAsync(AdGuardUrl);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }



        public async Task<bool> CheckRouterLoginAsync()
        {
            try
            {
                var request = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "call",

                    // params is a reserved C# word, so use @params
                    @params = new object[]
                    {
                        "",
                        "system",
                        "get_info",
                        new { }
                    }
                };


                string json = JsonSerializer.Serialize(request);


                using var content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");


                using var response = await _client.PostAsync(
                    "http://192.168.1.1/rpc",
                    content);


                string result = await response.Content.ReadAsStringAsync();


                return result.Contains("\"hostname\"");
            }
            catch
            {
                return false;
            }
        }



        private void OpenBrowser(string url)
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
        }
    }
}