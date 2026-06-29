using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

public sealed class GetUserSubscriptions
{
    private readonly ApimManagementService _apim;
    private readonly RequestAuthService _auth;
    private readonly ILogger<GetUserSubscriptions> _logger;

    public GetUserSubscriptions(ApimManagementService apim, RequestAuthService auth, ILogger<GetUserSubscriptions> logger)
    {
        _apim = apim;
        _auth = auth;
        _logger = logger;
    }

    [Function("GetUserSubscriptions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "user-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // The owner is resolved server-side from the authenticated user, so a caller only ever sees
        // the subscriptions they own.
        _logger.LogInformation("Listing user subscriptions for user {UserId}", result.UserId);
        var subscriptions = await _apim.ListUserSubscriptionsAsync(result.UserId, ct);

        // Optional product filter: when the widget is hosted on a product page it passes ?productId=,
        // so the grid only shows subscriptions scoped to that product.
        var productId = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("productId");
        if (!string.IsNullOrWhiteSpace(productId))
        {
            subscriptions = subscriptions
                .Where(s => string.Equals(ApimManagementService.ParseProductId(s.Scope), productId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(subscriptions, ct);
        return response;
    }
}
