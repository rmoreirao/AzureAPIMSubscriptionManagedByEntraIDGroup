using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

/// <summary>
/// Manage (regenerate keys / cancel) an APIM subscription owned by the authenticated Dev Portal user.
/// Mirrors <see cref="ManageGroupSubscriptionApim"/> but authorizes via subscription <b>ownership</b>
/// rather than group membership, so a caller may only ever act on their own subscriptions.
/// </summary>
public sealed class ManageUserSubscription
{
    private readonly ApimManagementService _apim;
    private readonly RequestAuthService _auth;
    private readonly ILogger<ManageUserSubscription> _logger;

    public ManageUserSubscription(ApimManagementService apim, RequestAuthService auth, ILogger<ManageUserSubscription> logger)
    {
        _apim = apim;
        _auth = auth;
        _logger = logger;
    }

    [Function("ManageUserSubscription")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "user-subscriptions/{subscriptionId}/{action}")] HttpRequestData req,
        string subscriptionId,
        string action,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // The caller may only manage subscriptions they own.
        if (!await _apim.IsOwnedByUserAsync(subscriptionId, result.UserId, ct))
        {
            _logger.LogWarning("User {UserId} does not own subscription {SubscriptionId}", result.UserId, subscriptionId);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        switch (action.ToLowerInvariant())
        {
            case "regenerate":
                _logger.LogInformation("Regenerating keys for user APIM subscription {SubscriptionId}", subscriptionId);
                var keys = await _apim.RegenerateKeysAsync(subscriptionId, ct);
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(keys, ct);
                return ok;

            case "cancel":
                _logger.LogInformation("Cancelling user APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.CancelSubscriptionAsync(subscriptionId, ct);
                return req.CreateResponse(HttpStatusCode.NoContent);

            default:
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("action must be 'regenerate' or 'cancel'.", ct);
                return bad;
        }
    }
}
