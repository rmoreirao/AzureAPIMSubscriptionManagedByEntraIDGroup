namespace GroupSubscriptions.Functions.Services;

/// <summary>
/// Enforces an APIM Product's "Subscriptions limit" (Max Subscriptions) when subscriptions are created
/// through the management API, which — unlike the Dev Portal — does not apply the limit itself. The same
/// product limit is enforced independently per <b>user</b> and per <b>team/group</b>.
/// </summary>
public sealed class SubscriptionLimitService
{
    private readonly ApimManagementService _apim;

    public SubscriptionLimitService(ApimManagementService apim)
    {
        _apim = apim;
    }

    public enum OwnerKind
    {
        User,
        Team
    }

    public sealed class LimitDecision
    {
        public bool Allowed { get; init; }
        public string? Message { get; init; }

        public static LimitDecision Allow() => new() { Allowed = true };
        public static LimitDecision Deny(string message) => new() { Allowed = false, Message = message };
    }

    /// <summary>
    /// Decides whether a new subscription may be created for <paramref name="productId"/> given the
    /// <paramref name="currentCount"/> the owner already holds. Returns <see cref="LimitDecision.Allow"/>
    /// when the product has no limit configured; otherwise denies (with an explanatory message) once the
    /// owner's count reaches the limit.
    /// </summary>
    public async Task<LimitDecision> EvaluateAsync(string productId, int currentCount, OwnerKind owner, CancellationToken ct = default)
    {
        var limit = await _apim.GetProductSubscriptionsLimitAsync(productId, ct);
        if (limit is null)
        {
            // No limit configured on the product ⇒ unlimited.
            return LimitDecision.Allow();
        }

        if (currentCount < limit.Value)
        {
            return LimitDecision.Allow();
        }

        var productName = await _apim.GetProductDisplayNameAsync(productId, ct);
        var who = owner == OwnerKind.User ? "user" : "team";
        var message =
            $"The product '{productName}' allows a maximum of {limit.Value} subscription(s) per {who}. " +
            $"This {who} already has {currentCount}. Cancel an existing subscription for this product before creating a new one.";
        return LimitDecision.Deny(message);
    }
}
