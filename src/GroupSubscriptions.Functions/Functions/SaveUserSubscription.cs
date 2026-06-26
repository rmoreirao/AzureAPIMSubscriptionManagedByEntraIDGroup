using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

public sealed class SaveUserSubscription
{
    private readonly ApimManagementService _apim;
    private readonly SubscriptionLimitService _limits;
    private readonly RequestAuthService _auth;
    private readonly ILogger<SaveUserSubscription> _logger;

    public SaveUserSubscription(ApimManagementService apim, SubscriptionLimitService limits, RequestAuthService auth, ILogger<SaveUserSubscription> logger)
    {
        _apim = apim;
        _limits = limits;
        _auth = auth;
        _logger = logger;
    }

    [Function("SaveUserSubscription")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "user-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var request = await req.ReadFromJsonAsync<CreateUserSubscriptionRequest>(ct);
        if (request is null || string.IsNullOrWhiteSpace(request.SubscriptionName))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("subscriptionName is required.", ct);
            return bad;
        }

        var apimSubscriptionId = $"user-{Guid.NewGuid():N}";
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "/apis" : request.Scope;

        // A subscription must target a specific APIM product.
        var productId = ApimManagementService.ParseProductId(scope);
        if (productId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("A product is required to create a subscription. Provide a product scope (e.g. /products/{productId}).", ct);
            return bad;
        }

        // Enforce the product's Max Subscriptions limit for this user.
        var currentCount = await _apim.CountUserSubscriptionsForProductAsync(result.UserId, productId, ct);
        var decision = await _limits.EvaluateAsync(productId, currentCount, SubscriptionLimitService.OwnerKind.User, ct);
        if (!decision.Allowed)
        {
            _logger.LogWarning("User {UserId} reached the subscription limit for product {ProductId}", result.UserId, productId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteStringAsync(decision.Message!, ct);
            return conflict;
        }

        // The subscription is always owned by the authenticated caller — never trust an owner from the client.
        _logger.LogInformation("Creating user APIM subscription {SubscriptionId} for user {UserId}", apimSubscriptionId, result.UserId);
        string createdId;
        SubscriptionKeys keys;
        try
        {
            (createdId, keys) = await _apim.CreateUserSubscriptionAsync(apimSubscriptionId, request.SubscriptionName, result.UserId, scope, ct);
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

        var view = new UserSubscriptionView
        {
            SubscriptionId = createdId,
            SubscriptionName = request.SubscriptionName,
            State = "active",
            Scope = scope,
            DateCreated = DateTimeOffset.UtcNow,
            PrimaryKey = keys.PrimaryKey,
            SecondaryKey = keys.SecondaryKey
        };

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(view, ct);
        // WriteAsJsonAsync resets the status to 200 OK, so re-assert 201 Created.
        response.StatusCode = HttpStatusCode.Created;
        return response;
    }
}
