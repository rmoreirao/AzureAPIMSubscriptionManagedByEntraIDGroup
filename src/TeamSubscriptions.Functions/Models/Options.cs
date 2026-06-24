namespace TeamSubscriptions.Functions.Models;

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
