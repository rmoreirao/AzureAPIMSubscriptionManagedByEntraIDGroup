using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Security;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

/// <summary>
/// APIM-group variant of <see cref="ManageTeamSubscription"/>: the caller's membership is verified
/// against <b>APIM groups</b> instead of Entra ID.
/// </summary>
public sealed class ManageTeamSubscriptionApim
{
    private readonly ApimManagementService _apim;
    private readonly CosmosRepository _repository;
    private readonly ApimGroupService _groups;
    private readonly RequestAuthService _auth;
    private readonly ILogger<ManageTeamSubscriptionApim> _logger;

    public ManageTeamSubscriptionApim(ApimManagementService apim, CosmosRepository repository, ApimGroupService groups, RequestAuthService auth, ILogger<ManageTeamSubscriptionApim> logger)
    {
        _apim = apim;
        _repository = repository;
        _groups = groups;
        _auth = auth;
        _logger = logger;
    }

    [Function("ManageTeamSubscriptionApim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "apim/team-subscriptions/{entraIdGroup}/{subscriptionId}/{action}")] HttpRequestData req,
        string entraIdGroup,
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

        // The caller may only manage subscriptions belonging to an APIM group they are a member of.
        if (!await _groups.IsMemberOfGroupAsync(result.UserId, entraIdGroup, ct))
        {
            _logger.LogWarning("User {UserId} is not a member of APIM group {Group}", result.UserId, entraIdGroup);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        // Ensure the subscription actually belongs to the supplied group before acting on it.
        var records = await _repository.GetByGroupsAsync(new[] { entraIdGroup }, ct);
        var record = records.FirstOrDefault(r => r.SubscriptionId == subscriptionId);
        if (record is null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found for APIM group {Group}", subscriptionId, entraIdGroup);
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        switch (action.ToLowerInvariant())
        {
            case "regenerate":
                _logger.LogInformation("Regenerating keys for APIM subscription {SubscriptionId}", subscriptionId);
                var keys = await _apim.RegenerateKeysAsync(subscriptionId, ct);
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(keys, ct);
                return ok;

            case "cancel":
                _logger.LogInformation("Cancelling APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.CancelSubscriptionAsync(subscriptionId, ct);
                await _repository.DeleteAsync(record.Id, entraIdGroup, ct);
                return req.CreateResponse(HttpStatusCode.NoContent);

            default:
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("action must be 'regenerate' or 'cancel'.", ct);
                return bad;
        }
    }
}
