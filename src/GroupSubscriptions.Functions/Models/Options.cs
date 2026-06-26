namespace GroupSubscriptions.Functions.Models;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
}

public sealed class ApimOptions
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
}

public sealed class DevPortalOptions
{
    /// <summary>
    /// Base URL of the APIM Developer Portal (e.g. https://contoso.developer.azure-api.net).
    /// Incoming requests must carry an <c>xmh-origin</c> that contains this value.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
