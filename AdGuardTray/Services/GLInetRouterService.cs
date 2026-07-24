using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CryptSharp;

namespace AdGuardTray.Services
{
    public class GLInetRouterService
    {
        private readonly CookieContainer _cookies;
        private readonly HttpClient _client;


        public GLInetRouterService()
        {
            _cookies = new CookieContainer();

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,

                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            _client = new HttpClient(handler);

            _client.BaseAddress =
                new Uri("https://192.168.1.1/");
        }


        public async Task<string> GetChallengeAsync(string username)
        {
            string json =
$@"{{
    ""jsonrpc"": ""2.0"",
    ""id"": 1,
    ""method"": ""challenge"",
    ""params"": {{
        ""username"": ""{username}""
    }}
}}";


            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");


            HttpResponseMessage response =
                await _client.PostAsync(
                    "rpc",
                    content);


            return await response.Content.ReadAsStringAsync();
        }


        public async Task<string> LoginAsync(
            string username,
            string password)
        {
            string challenge =
                await GetChallengeAsync(username);


            using JsonDocument doc =
                JsonDocument.Parse(challenge);


            JsonElement result =
                doc.RootElement
                   .GetProperty("result");


            string salt =
                result.GetProperty("salt")
                      .GetString()!;


            string nonce =
                result.GetProperty("nonce")
                      .GetString()!;


            string cryptHash =
                Sha256Crypt(
                    password,
                    salt);


            string combined =
                $"{username}:{cryptHash}:{nonce}";


            string finalHash =
                Md5(combined);


            string loginJson =
$@"{{
    ""jsonrpc"": ""2.0"",
    ""id"": 2,
    ""method"": ""login"",
    ""params"": {{
        ""username"": ""{username}"",
        ""hash"": ""{finalHash}""
    }}
}}";


            var loginContent =
                new StringContent(
                    loginJson,
                    Encoding.UTF8,
                    "application/json");


            HttpResponseMessage response =
                await _client.PostAsync(
                    "rpc",
                    loginContent);


            return await response.Content.ReadAsStringAsync();
        }


        private string Sha256Crypt(
            string password,
            string salt)
        {
            string formattedSalt =
                $"$5${salt}$";


            return Crypter.Sha256.Crypt(
                password,
                formattedSalt);
        }


        private string Md5(string input)
        {
            using MD5 md5 = MD5.Create();


            byte[] bytes =
                md5.ComputeHash(
                    Encoding.UTF8.GetBytes(input));


            StringBuilder builder =
                new StringBuilder();


            foreach (byte b in bytes)
            {
                builder.Append(
                    b.ToString("x2"));
            }


            return builder.ToString();
        }
    }
}