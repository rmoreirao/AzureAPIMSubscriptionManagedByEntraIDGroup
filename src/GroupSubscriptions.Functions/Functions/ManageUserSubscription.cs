using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "user-subscriptions/{subscriptionId}/{action}")] HttpRequestData req,
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
            case "primary-key":
                var primary = await _apim.GetPrimaryKeyAsync(subscriptionId, ct);
                var primaryOk = req.CreateResponse(HttpStatusCode.OK);
                await primaryOk.WriteAsJsonAsync(new SubscriptionKeys { PrimaryKey = primary }, ct);
                return primaryOk;

            case "secondary-key":
                var secondary = await _apim.GetSecondaryKeyAsync(subscriptionId, ct);
                var secondaryOk = req.CreateResponse(HttpStatusCode.OK);
                await secondaryOk.WriteAsJsonAsync(new SubscriptionKeys { SecondaryKey = secondary }, ct);
                return secondaryOk;

            case "regenerate":
                _logger.LogInformation("Regenerating keys for user APIM subscription {SubscriptionId}", subscriptionId);
                var keys = await _apim.RegenerateKeysAsync(subscriptionId, ct);
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(keys, ct);
                return ok;

            case "regenerate-primary":
                _logger.LogInformation("Regenerating primary key for user APIM subscription {SubscriptionId}", subscriptionId);
                var newPrimary = await _apim.RegeneratePrimaryKeyAsync(subscriptionId, ct);
                var primaryRegenOk = req.CreateResponse(HttpStatusCode.OK);
                await primaryRegenOk.WriteAsJsonAsync(new SubscriptionKeys { PrimaryKey = newPrimary }, ct);
                return primaryRegenOk;

            case "regenerate-secondary":
                _logger.LogInformation("Regenerating secondary key for user APIM subscription {SubscriptionId}", subscriptionId);
                var newSecondary = await _apim.RegenerateSecondaryKeyAsync(subscriptionId, ct);
                var secondaryRegenOk = req.CreateResponse(HttpStatusCode.OK);
                await secondaryRegenOk.WriteAsJsonAsync(new SubscriptionKeys { SecondaryKey = newSecondary }, ct);
                return secondaryRegenOk;

            case "cancel":
                _logger.LogInformation("Cancelling user APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.CancelSubscriptionAsync(subscriptionId, ct);
                return req.CreateResponse(HttpStatusCode.NoContent);

            case "rename":
                var renameBody = await req.ReadFromJsonAsync<RenameRequest>(ct);
                var newName = renameBody?.Name?.Trim();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    var badName = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badName.WriteStringAsync("A non-empty 'name' is required.", ct);
                    return badName;
                }
                _logger.LogInformation("Renaming user APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.RenameSubscriptionAsync(subscriptionId, newName, ct);
                var renameOk = req.CreateResponse(HttpStatusCode.OK);
                await renameOk.WriteAsJsonAsync(new { name = newName }, ct);
                return renameOk;

            default:
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("action must be 'primary-key', 'secondary-key', 'regenerate', 'regenerate-primary', 'regenerate-secondary', 'rename' or 'cancel'.", ct);
                return bad;
        }
    }
}
