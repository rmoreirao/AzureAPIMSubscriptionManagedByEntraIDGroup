using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

/// <summary>
/// Manages a Group subscription (keys, regenerate, cancel, rename). The owning APIM group is
/// derived from the persisted Cosmos record (looked up by <c>subscriptionId</c>) — never from client
/// input — and the caller's membership of that group is verified before any action runs.
/// </summary>
public sealed class ManageGroupSubscriptionApim
{
    private readonly ApimManagementService _apim;
    private readonly CosmosRepository _repository;
    private readonly ApimGroupService _groups;
    private readonly RequestAuthService _auth;
    private readonly ILogger<ManageGroupSubscriptionApim> _logger;

    public ManageGroupSubscriptionApim(ApimManagementService apim, CosmosRepository repository, ApimGroupService groups, RequestAuthService auth, ILogger<ManageGroupSubscriptionApim> logger)
    {
        _apim = apim;
        _repository = repository;
        _groups = groups;
        _auth = auth;
        _logger = logger;
    }

    [Function("ManageGroupSubscriptionApim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "apim/group-subscriptions/{subscriptionId}/{action}")] HttpRequestData req,
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

        // Resolve the owning APIM group from the persisted record (by subscriptionId), never from
        // client input. Unknown subscription -> 404.
        var record = await _repository.GetBySubscriptionIdAsync(subscriptionId, ct);
        if (record is null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found", subscriptionId);
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        // The caller may only manage subscriptions belonging to an APIM group they are a member of.
        if (!await _groups.IsMemberOfGroupAsync(result.UserId, record.ApimGroup, ct))
        {
            _logger.LogWarning("User {UserId} is not a member of APIM group {Group}", result.UserId, record.ApimGroup);
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
                _logger.LogInformation("Regenerating keys for APIM subscription {SubscriptionId}", subscriptionId);
                var keys = await _apim.RegenerateKeysAsync(subscriptionId, ct);
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(keys, ct);
                return ok;

            case "regenerate-primary":
                _logger.LogInformation("Regenerating primary key for APIM subscription {SubscriptionId}", subscriptionId);
                var newPrimary = await _apim.RegeneratePrimaryKeyAsync(subscriptionId, ct);
                var primaryRegenOk = req.CreateResponse(HttpStatusCode.OK);
                await primaryRegenOk.WriteAsJsonAsync(new SubscriptionKeys { PrimaryKey = newPrimary }, ct);
                return primaryRegenOk;

            case "regenerate-secondary":
                _logger.LogInformation("Regenerating secondary key for APIM subscription {SubscriptionId}", subscriptionId);
                var newSecondary = await _apim.RegenerateSecondaryKeyAsync(subscriptionId, ct);
                var secondaryRegenOk = req.CreateResponse(HttpStatusCode.OK);
                await secondaryRegenOk.WriteAsJsonAsync(new SubscriptionKeys { SecondaryKey = newSecondary }, ct);
                return secondaryRegenOk;

            case "cancel":
                _logger.LogInformation("Cancelling APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.CancelSubscriptionAsync(subscriptionId, ct);
                // Keep the Cosmos record so the cancelled subscription still appears in the grid
                // (state reflected as "Cancelled" from APIM), consistent with user subscriptions.
                // Mark it cancelled so it no longer counts against the product subscription limit.
                record.Status = "cancelled";
                await _repository.UpsertAsync(record, ct);
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
                _logger.LogInformation("Renaming APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.RenameSubscriptionAsync(subscriptionId, newName, ct);
                record.SubscriptionName = newName;
                await _repository.UpsertAsync(record, ct);
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
