namespace Expenses.Api.Auth;

public class AuthOptions
{
    public MicrosoftAuthOptions Microsoft { get; set; } = new();
}

public class MicrosoftAuthOptions
{
    // The Entra app registration's Application (client) ID. Non-secret; set in config.
    public string ClientId { get; set; } = "";

    // "common" allows both personal Microsoft accounts and any org account (SPEC decision #43/#44).
    public string Authority { get; set; } = "https://login.microsoftonline.com/common/v2.0";
}
