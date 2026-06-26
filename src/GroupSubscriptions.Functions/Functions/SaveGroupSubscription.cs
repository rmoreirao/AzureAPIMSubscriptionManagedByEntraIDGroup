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
    private readonly SubscriptionLimitService _limits;
    private readonly RequestAuthService _auth;
    private readonly ILogger<SaveGroupSubscription> _logger;

    public SaveGroupSubscription(ApimManagementService apim, CosmosRepository repository, GraphService graph, SubscriptionLimitService limits, RequestAuthService auth, ILogger<SaveGroupSubscription> logger)
    {
        _apim = apim;
        _repository = repository;
        _graph = graph;
        _limits = limits;
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

        // A subscription must target a specific APIM product.
        var productId = ApimManagementService.ParseProductId(scope);
        if (productId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("A product is required to create a subscription. Provide a product scope (e.g. /products/{productId}).", ct);
            return bad;
        }

        // Enforce the product's Max Subscriptions limit for this team/group.
        var currentCount = await _repository.CountByGroupAndProductAsync(request.EntraIdGroup, productId, ct);
        var decision = await _limits.EvaluateAsync(productId, currentCount, SubscriptionLimitService.OwnerKind.Team, ct);
        if (!decision.Allowed)
        {
            _logger.LogWarning("Group {Group} reached the subscription limit for product {ProductId}", request.EntraIdGroup, productId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteStringAsync(decision.Message!, ct);
            return conflict;
        }

        _logger.LogInformation("Creating APIM subscription {SubscriptionId} for group {Group}", apimSubscriptionId, request.EntraIdGroup);
        string createdId;
        SubscriptionKeys keys;
        try
        {
            (createdId, keys) = await _apim.CreateSubscriptionAsync(apimSubscriptionId, request.SubscriptionName, scope, ct);
        }
        catch (ApimSubscriptionLimitException ex)
        {
            _logger.LogWarning(ex, "APIM rejected subscription creation for product {ProductId} (limit reached)", productId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteStringAsync($"Cannot create the subscription: the product's maximum number of subscriptions has been reached. ({ex.Message})", ct);
            return conflict;
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "APIM rejected subscription creation for product {ProductId}", productId);
            var gateway = req.CreateResponse(HttpStatusCode.BadGateway);
            await gateway.WriteStringAsync($"APIM rejected the subscription request: {ex.Message}", ct);
            return gateway;
        }

        var record = new GroupSubscription
        {
            GroupId = request.EntraIdGroup,
            GroupName = request.GroupName,
            SubscriptionId = createdId,
            SubscriptionName = request.SubscriptionName,
            EntraIdGroup = request.EntraIdGroup,
            Scope = scope,
            ProductId = productId
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
