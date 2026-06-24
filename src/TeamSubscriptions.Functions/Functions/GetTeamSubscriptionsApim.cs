using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Models;
using TeamSubscriptions.Functions.Security;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

/// <summary>
/// APIM-group variant of <see cref="GetTeamSubscriptions"/>: resolves the caller's groups from
/// <b>APIM groups</b> instead of Entra ID.
/// </summary>
public sealed class GetTeamSubscriptionsApim
{
    private readonly CosmosRepository _repository;
    private readonly ApimGroupService _groups;
    private readonly ApimManagementService _apim;
    private readonly RequestAuthService _auth;
    private readonly ILogger<GetTeamSubscriptionsApim> _logger;

    public GetTeamSubscriptionsApim(CosmosRepository repository, ApimGroupService groups, ApimManagementService apim, RequestAuthService auth, ILogger<GetTeamSubscriptionsApim> logger)
    {
        _repository = repository;
        _groups = groups;
        _apim = apim;
        _auth = auth;
        _logger = logger;
    }

    [Function("GetTeamSubscriptionsApim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apim/team-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // The caller's groups are resolved server-side from the authenticated user — never trusted
        // from client input — so a user can only ever see subscriptions for APIM groups they belong to.
        var groups = await _groups.GetUserGroupsAsync(result.UserId, ct);
        var groupIds = groups.Select(g => g.Id).ToArray();

        _logger.LogInformation("Listing team subscriptions for user {UserId} across {Count} APIM group(s)", result.UserId, groupIds.Length);
        var subscriptions = await _repository.GetByGroupsAsync(groupIds, ct);

        // Enrich each record with its current APIM keys for display in the View/Modify widget.
        var views = new List<TeamSubscriptionView>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var keys = await _apim.GetKeysAsync(subscription.SubscriptionId, ct);
            views.Add(TeamSubscriptionView.From(subscription, keys));
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(views, ct);
        return response;
    }
}
