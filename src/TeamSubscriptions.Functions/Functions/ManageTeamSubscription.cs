using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Services;

namespace TeamSubscriptions.Functions.Functions;

public sealed class ManageTeamSubscription
{
    private readonly ApimManagementService _apim;
    private readonly CosmosRepository _repository;
    private readonly ILogger<ManageTeamSubscription> _logger;

    public ManageTeamSubscription(ApimManagementService apim, CosmosRepository repository, ILogger<ManageTeamSubscription> logger)
    {
        _apim = apim;
        _repository = repository;
        _logger = logger;
    }

    [Function("ManageTeamSubscription")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "team-subscriptions/{entraIdGroup}/{subscriptionId}/{action}")] HttpRequestData req,
        string entraIdGroup,
        string subscriptionId,
        string action,
        CancellationToken ct)
    {
        switch (action.ToLowerInvariant())
        {
            case "regenerate":
                _logger.LogInformation("Regenerating keys for APIM subscription {SubscriptionId}", subscriptionId);
                var keys = await _apim.RegenerateKeysAsync(subscriptionId, ct);
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(keys, ct);
                return ok;

            case "cancel":
                _logger.LogInformation("Cancelling APIM subscription {SubscriptionId}", subscriptionId);
                await _apim.CancelSubscriptionAsync(subscriptionId, ct);

                var records = await _repository.GetByGroupsAsync(new[] { entraIdGroup }, ct);
                var record = records.FirstOrDefault(r => r.SubscriptionId == subscriptionId);
                if (record is not null)
                {
                    await _repository.DeleteAsync(record.Id, entraIdGroup, ct);
                }

                return req.CreateResponse(HttpStatusCode.NoContent);

            default:
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("action must be 'regenerate' or 'cancel'.", ct);
                return bad;
        }
    }
}
