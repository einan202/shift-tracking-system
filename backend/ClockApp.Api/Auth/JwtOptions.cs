namespace ClockApp.Api.Auth;

/// <summary>JWT settings (section "Jwt"). The secret comes from the environment, never from source control.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinSecretLength = 32;

    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "clockapp";
    public string Audience { get; set; } = "clockapp";

    /// <summary>Short-lived tokens: deactivation / role changes take effect quickly.</summary>
    public int LifetimeMinutes { get; set; } = 45;
}
