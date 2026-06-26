using System.Text.Json.Serialization;

namespace GroupSubscriptions.Functions.Models;

/// <summary>Document persisted in Cosmos DB representing a Group subscription.</summary>
public sealed class GroupSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("groupName")]
    public string GroupName { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionName")]
    public string SubscriptionName { get; set; } = string.Empty;

    [JsonPropertyName("entraIdGroup")]
    public string EntraIdGroup { get; set; } = string.Empty;

    [JsonPropertyName("dateCreated")]
    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CreateGroupSubscriptionRequest
{
    public string SubscriptionName { get; set; } = string.Empty;
    public string EntraIdGroup { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

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

/// <summary>A Group subscription enriched with its current APIM keys, for the list/view widget.</summary>
public sealed class GroupSubscriptionView
{
    public string Id { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string EntraIdGroup { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTimeOffset DateCreated { get; set; }
    public string? PrimaryKey { get; set; }
    public string? SecondaryKey { get; set; }

    public static GroupSubscriptionView From(GroupSubscription s, SubscriptionKeys keys) => new()
    {
        Id = s.Id,
        SubscriptionId = s.SubscriptionId,
        SubscriptionName = s.SubscriptionName,
        EntraIdGroup = s.EntraIdGroup,
        GroupName = s.GroupName,
        DateCreated = s.DateCreated,
        PrimaryKey = keys.PrimaryKey,
        SecondaryKey = keys.SecondaryKey
    };

    public static GroupSubscriptionView From(GroupSubscription s, SubscriptionDetails details) => new()
    {
        Id = s.Id,
        SubscriptionId = s.SubscriptionId,
        SubscriptionName = s.SubscriptionName,
        EntraIdGroup = s.EntraIdGroup,
        GroupName = s.GroupName,
        State = details.State,
        Scope = details.Scope,
        Product = details.Product,
        DateCreated = s.DateCreated,
        PrimaryKey = details.PrimaryKey,
        SecondaryKey = details.SecondaryKey
    };
}
