using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

public sealed class GetGroupSubscriptions
{
    private readonly CosmosRepository _repository;
    private readonly GraphService _graph;
    private readonly ApimManagementService _apim;
    private readonly RequestAuthService _auth;
    private readonly ILogger<GetGroupSubscriptions> _logger;

    public GetGroupSubscriptions(CosmosRepository repository, GraphService graph, ApimManagementService apim, RequestAuthService auth, ILogger<GetGroupSubscriptions> logger)
    {
        _repository = repository;
        _graph = graph;
        _apim = apim;
        _auth = auth;
        _logger = logger;
    }

    [Function("GetGroupSubscriptions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "group-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // The caller's groups are resolved server-side from the authenticated user — never trusted
        // from client input — so a user can only ever see subscriptions for groups they belong to.
        var groups = await _graph.GetUserGroupsAsync(result.UserId, ct);
        var groupIds = groups.Select(g => g.Id).ToArray();

        _logger.LogInformation("Listing Group subscriptions for user {UserId} across {Count} group(s)", result.UserId, groupIds.Length);
        var subscriptions = await _repository.GetByGroupsAsync(groupIds, ct);

        // Enrich each record with its current APIM state, product and keys for display in the table.
        var views = new List<GroupSubscriptionView>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var details = await _apim.GetSubscriptionDetailsAsync(subscription.SubscriptionId, ct);
            views.Add(GroupSubscriptionView.From(subscription, details));
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(views, ct);
        return response;
    }
}
