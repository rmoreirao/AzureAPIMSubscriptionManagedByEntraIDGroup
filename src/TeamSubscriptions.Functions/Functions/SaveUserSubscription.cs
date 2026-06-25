using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Models;
using TeamSubscriptions.Functions.Security;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

public sealed class SaveUserSubscription
{
    private readonly ApimManagementService _apim;
    private readonly RequestAuthService _auth;
    private readonly ILogger<SaveUserSubscription> _logger;

    public SaveUserSubscription(ApimManagementService apim, RequestAuthService auth, ILogger<SaveUserSubscription> logger)
    {
        _apim = apim;
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

        // The subscription is always owned by the authenticated caller — never trust an owner from the client.
        _logger.LogInformation("Creating user APIM subscription {SubscriptionId} for user {UserId}", apimSubscriptionId, result.UserId);
        var (createdId, keys) = await _apim.CreateUserSubscriptionAsync(apimSubscriptionId, request.SubscriptionName, result.UserId, scope, ct);

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
