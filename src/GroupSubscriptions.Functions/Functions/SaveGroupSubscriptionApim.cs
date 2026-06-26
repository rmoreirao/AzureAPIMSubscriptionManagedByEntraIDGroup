using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

/// <summary>
/// APIM-group variant of <see cref="SaveGroupSubscription"/>: the caller's membership is verified
/// against <b>APIM groups</b> instead of Entra ID. The APIM group id is stored in the existing
/// group fields of the Cosmos record.
/// </summary>
public sealed class SaveGroupSubscriptionApim
{
    private readonly ApimManagementService _apim;
    private readonly CosmosRepository _repository;
    private readonly ApimGroupService _groups;
    private readonly SubscriptionLimitService _limits;
    private readonly RequestAuthService _auth;
    private readonly ILogger<SaveGroupSubscriptionApim> _logger;

    public SaveGroupSubscriptionApim(ApimManagementService apim, CosmosRepository repository, ApimGroupService groups, SubscriptionLimitService limits, RequestAuthService auth, ILogger<SaveGroupSubscriptionApim> logger)
    {
        _apim = apim;
        _repository = repository;
        _groups = groups;
        _limits = limits;
        _auth = auth;
        _logger = logger;
    }

    [Function("SaveGroupSubscriptionApim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "apim/group-subscriptions")] HttpRequestData req,
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

        // The caller may only create a Group subscription for an APIM group they are a member of.
        if (!await _groups.IsMemberOfGroupAsync(result.UserId, request.EntraIdGroup, ct))
        {
            _logger.LogWarning("User {UserId} is not a member of APIM group {Group}", result.UserId, request.EntraIdGroup);
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
            _logger.LogWarning("APIM group {Group} reached the subscription limit for product {ProductId}", request.EntraIdGroup, productId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteStringAsync(decision.Message!, ct);
            return conflict;
        }

        _logger.LogInformation("Creating APIM subscription {SubscriptionId} for APIM group {Group}", apimSubscriptionId, request.EntraIdGroup);
        var (createdId, keys) = await _apim.CreateSubscriptionAsync(apimSubscriptionId, request.SubscriptionName, scope, ct);

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
