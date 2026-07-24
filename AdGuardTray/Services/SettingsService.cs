using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public class SettingsService
    {
        private readonly string _settingsFolder;
        private readonly string _settingsFile;

        public SettingsService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AdGuardTray");

            _settingsFile = Path.Combine(_settingsFolder, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                    Directory.CreateDirectory(_settingsFolder);

                if (!File.Exists(_settingsFile))
                {
                    var settings = new AppSettings();
                    Save(settings);
                    return settings;
                }

                var json = File.ReadAllText(_settingsFile);

                return JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (!Directory.Exists(_settingsFolder))
                Directory.CreateDirectory(_settingsFolder);

            var json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(_settingsFile, json);
        }

        public string EncryptPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return "";

            byte[] bytes = Encoding.UTF8.GetBytes(password);

            byte[] encrypted = ProtectedData.Protect(
                bytes,
                null,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encrypted);
        }

        public string DecryptPassword(string encryptedPassword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(encryptedPassword))
                    return "";

                byte[] bytes = Convert.FromBase64String(encryptedPassword);

                byte[] decrypted = ProtectedData.Unprotect(
                    bytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return "";
            }
        }
    }
}