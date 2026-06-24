using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

public sealed class GetUserGroups
{
    private readonly GraphService _graph;
    private readonly ILogger<GetUserGroups> _logger;

    public GetUserGroups(GraphService graph, ILogger<GetUserGroups> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    [Function("GetUserGroups")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/groups")] HttpRequestData req,
        string userId,
        CancellationToken ct)
    {
        _logger.LogInformation("Fetching Entra ID groups for user {UserId}", userId);
        var groups = await _graph.GetUserGroupsAsync(userId, ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(groups, ct);
        return response;
    }
}
