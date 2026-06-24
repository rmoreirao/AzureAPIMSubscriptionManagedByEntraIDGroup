using System.Text.Json.Serialization;

namespace TeamSubscriptions.Functions.Models;

/// <summary>Document persisted in Cosmos DB representing a team subscription.</summary>
public sealed class TeamSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("teamId")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("teamName")]
    public string TeamName { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionName")]
    public string SubscriptionName { get; set; } = string.Empty;

    [JsonPropertyName("entraIdGroup")]
    public string EntraIdGroup { get; set; } = string.Empty;

    [JsonPropertyName("dateCreated")]
    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CreateTeamSubscriptionRequest
{
    public string SubscriptionName { get; set; } = string.Empty;
    public string EntraIdGroup { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;

    /// <summary>APIM scope for the standalone subscription (e.g. /products/{id} or /apis).</summary>
    public string Scope { get; set; } = string.Empty;
}

public sealed class EntraGroup
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SubscriptionKeys
{
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }
}
