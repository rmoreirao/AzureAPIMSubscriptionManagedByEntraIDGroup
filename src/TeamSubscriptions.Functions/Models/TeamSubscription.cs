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

public sealed class CreateUserSubscriptionRequest
{
    public string SubscriptionName { get; set; } = string.Empty;

    /// <summary>APIM scope for the subscription (e.g. /products/{id} or /apis).</summary>
    public string Scope { get; set; } = string.Empty;
}

/// <summary>An APIM subscription owned by a single Dev Portal user, for the user-subscription widget panel.</summary>
public sealed class UserSubscriptionView
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTimeOffset? DateCreated { get; set; }
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }
}

public sealed class SubscriptionKeys
{
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }
}

/// <summary>The live state, scope/product and keys of an APIM subscription, used to enrich list views.</summary>
public sealed class SubscriptionDetails
{
    public string State { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }
}

/// <summary>A team subscription enriched with its current APIM keys, for the list/view widget.</summary>
public sealed class TeamSubscriptionView
{
    public string Id { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string EntraIdGroup { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTimeOffset DateCreated { get; set; }
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }

    public static TeamSubscriptionView From(TeamSubscription s, SubscriptionKeys keys) => new()
    {
        Id = s.Id,
        SubscriptionId = s.SubscriptionId,
        SubscriptionName = s.SubscriptionName,
        EntraIdGroup = s.EntraIdGroup,
        TeamName = s.TeamName,
        DateCreated = s.DateCreated,
        PrimaryKey = keys.PrimaryKey,
        SecondaryKey = keys.SecondaryKey
    };

    public static TeamSubscriptionView From(TeamSubscription s, SubscriptionDetails details) => new()
    {
        Id = s.Id,
        SubscriptionId = s.SubscriptionId,
        SubscriptionName = s.SubscriptionName,
        EntraIdGroup = s.EntraIdGroup,
        TeamName = s.TeamName,
        State = details.State,
        Scope = details.Scope,
        Product = details.Product,
        DateCreated = s.DateCreated,
        PrimaryKey = details.PrimaryKey,
        SecondaryKey = details.SecondaryKey
    };
}
