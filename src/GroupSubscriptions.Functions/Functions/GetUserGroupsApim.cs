using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

namespace GroupSubscriptions.Functions.Functions;

/// <summary>
/// Lists the caller's <b>APIM groups</b> (custom groups eligible for group subscriptions).
/// </summary>
public sealed class GetUserGroupsApim
{
    private readonly ApimGroupService _groups;
    private readonly RequestAuthService _auth;
    private readonly ILogger<GetUserGroupsApim> _logger;

    public GetUserGroupsApim(ApimGroupService groups, RequestAuthService auth, ILogger<GetUserGroupsApim> logger)
    {
        _groups = groups;
        _auth = auth;
        _logger = logger;
    }

    [Function("GetUserGroupsApim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apim/users/{userId}/groups")] HttpRequestData req,
        string userId,
        CancellationToken ct)
    {
        var context = HttpRequestReader.GetInvocationContext(req);
        var result = await _auth.AuthenticateAsync(context, ct);
        if (!result.IsAuthorized)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // Users may only list their own groups.
        if (!userId.Equals(result.UserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("User {Caller} attempted to read APIM groups for {Target}", result.UserId, userId);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        _logger.LogInformation("Fetching APIM groups for user {UserId}", result.UserId);
        var groups = await _groups.GetUserGroupsAsync(result.UserId, ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(groups, ct);
        return response;
    }
}
