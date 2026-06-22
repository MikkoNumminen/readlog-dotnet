namespace ReadLog.Tests.Infrastructure;

/// <summary>HttpClient helpers for driving the antiforgery-protected Razor Pages from tests.</summary>
public static class WebTestClient
{
    /// <summary>Registers (and thereby signs in) a new user on this client.</summary>
    public static async Task RegisterAsync(
        this HttpClient client, string email, string password = "Password1", string? name = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["ReturnUrl"] = "/",
        };
        if (name is not null)
        {
            fields["Input.Name"] = name;
        }

        var response = await client.PostFormAsync("/register", fields);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GETs <paramref name="url"/> to capture the antiforgery token + cookie, then POSTs the form there.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string url, IDictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(url);
        var data = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = HtmlFormHelper.ExtractAntiforgeryToken(html),
        };
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }
}
