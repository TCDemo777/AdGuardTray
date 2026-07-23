using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AdGuardTray.Services
{
    public class AdGuardService
    {
        private readonly HttpClient _client;


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
    }
}