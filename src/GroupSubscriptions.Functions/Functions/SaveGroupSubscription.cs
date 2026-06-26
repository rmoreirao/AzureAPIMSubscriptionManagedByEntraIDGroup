using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

public sealed class SaveGroupSubscription
{
    private readonly ApimManagementService _apim;
    private readonly CosmosRepository _repository;
    private readonly GraphService _graph;
    private readonly RequestAuthService _auth;
    private readonly ILogger<SaveGroupSubscription> _logger;

    public SaveGroupSubscription(ApimManagementService apim, CosmosRepository repository, GraphService graph, RequestAuthService auth, ILogger<SaveGroupSubscription> logger)
    {
        _apim = apim;
        _repository = repository;
        _graph = graph;
        _auth = auth;
        _logger = logger;
    }

    [Function("SaveGroupSubscription")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "group-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var request = await req.ReadFromJsonAsync<CreateGroupSubscriptionRequest>(ct);
        if (request is null || string.IsNullOrWhiteSpace(request.SubscriptionName) || string.IsNullOrWhiteSpace(request.EntraIdGroup))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("subscriptionName and entraIdGroup are required.", ct);
            return bad;
        }

        // The caller may only create a Group subscription for a group they are a member of.
        if (!await _graph.IsMemberOfGroupAsync(result.UserId, request.EntraIdGroup, ct))
        {
            _logger.LogWarning("User {UserId} is not a member of group {Group}", result.UserId, request.EntraIdGroup);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        var apimSubscriptionId = $"group-{Guid.NewGuid():N}";
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? "/apis"
            : request.Scope;

        _logger.LogInformation("Creating APIM subscription {SubscriptionId} for group {Group}", apimSubscriptionId, request.EntraIdGroup);
        var (createdId, keys) = await _apim.CreateSubscriptionAsync(apimSubscriptionId, request.SubscriptionName, scope, ct);

        var record = new GroupSubscription
        {
            GroupId = request.EntraIdGroup,
            GroupName = request.GroupName,
            SubscriptionId = createdId,
            SubscriptionName = request.SubscriptionName,
            EntraIdGroup = request.EntraIdGroup
        };
        await _repository.UpsertAsync(record, ct);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            subscription = record,
            keys
        }, ct);
        // WriteAsJsonAsync resets the status to 200 OK, so re-assert 201 Created.
        response.StatusCode = HttpStatusCode.Created;
        return response;
    }
}
