using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AdGuardTray.Services
{
    public class AdGuardService
    {
        private readonly HttpClient _client;

        private readonly string _baseUrl =
            "http://192.168.1.1:3000";


        public AdGuardService()
        {
            _client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }


        public async Task<bool> IsAvailableAsync(string address)
        {
            try
            {
                HttpResponseMessage response =
                    await _client.GetAsync(address);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }


        public async Task<string?> GetStatusAsync(
            string username,
            string password)
        {
            try
            {
                string credentials =
                    Convert.ToBase64String(
                        Encoding.ASCII.GetBytes(
                            $"{username}:{password}"
                        ));

                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Basic",
                        credentials);


                HttpResponseMessage response =
                    await _client.GetAsync(
                        $"{_baseUrl}/control/status"
                    );


                if (!response.IsSuccessStatusCode)
                {
                    return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                }


                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}