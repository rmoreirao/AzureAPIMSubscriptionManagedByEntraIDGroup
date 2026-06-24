using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

public sealed class GetTeamSubscriptions
{
    private readonly CosmosRepository _repository;
    private readonly ILogger<GetTeamSubscriptions> _logger;

    public GetTeamSubscriptions(CosmosRepository repository, ILogger<GetTeamSubscriptions> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [Function("GetTeamSubscriptions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "team-subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        // Caller passes the user's group ids (resolved via GetUserGroups) as a comma-separated list.
        var query = HttpUtility.ParseQueryString(req.Url.Query);
        var groups = (query["groups"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("Listing team subscriptions for {Count} group(s)", groups.Length);
        var subscriptions = await _repository.GetByGroupsAsync(groups, ct);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(subscriptions, ct);
        return response;
    }
}
