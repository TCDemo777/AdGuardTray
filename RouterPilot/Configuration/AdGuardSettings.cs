namespace RouterPilot.Configuration;

public class AdGuardSettings
{
    public string BaseUrl { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public int RefreshSeconds { get; set; } = 15;
}