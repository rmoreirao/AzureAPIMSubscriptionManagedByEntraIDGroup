using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Security;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

public sealed class GetUserGroups
{
    private readonly GraphService _graph;
    private readonly RequestAuthService _auth;
    private readonly ILogger<GetUserGroups> _logger;

    public GetUserGroups(GraphService graph, RequestAuthService auth, ILogger<GetUserGroups> logger)
    {
        _graph = graph;
        _auth = auth;
        _logger = logger;
    }

    [Function("GetUserGroups")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/groups")] HttpRequestData req,
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
            _logger.LogWarning("User {Caller} attempted to read groups for {Target}", result.UserId, userId);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        _logger.LogInformation("Fetching Entra ID groups for user {UserId}", result.UserId);
        var groups = await _graph.GetUserGroupsAsync(result.UserId, ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(groups, ct);
        return response;
    }
}
