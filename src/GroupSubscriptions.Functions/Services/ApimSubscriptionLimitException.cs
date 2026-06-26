namespace GroupSubscriptions.Functions.Services;

/// <summary>
/// Thrown when APIM rejects a subscription creation because the target product's "Subscriptions limit"
/// (Max Subscriptions) has been reached. Endpoints translate this into an HTTP 409 Conflict with a
/// client-friendly message instead of letting the raw <see cref="Azure.RequestFailedException"/> surface
/// as an opaque 500.
/// </summary>
public sealed class ApimSubscriptionLimitException : Exception
{
    public ApimSubscriptionLimitException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
