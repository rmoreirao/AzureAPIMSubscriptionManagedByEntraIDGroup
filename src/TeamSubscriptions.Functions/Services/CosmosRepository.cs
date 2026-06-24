using Microsoft.Azure.Cosmos;
using TeamSubscriptions.Functions.Models;

namespace TeamSubscriptions.Functions.Services;

public sealed class CosmosRepository
{
    private readonly Container _container;

    public CosmosRepository(CosmosClient client, CosmosOptions options)
    {
        _container = client.GetContainer(options.Database, options.Container);
    }

    public async Task<TeamSubscription> UpsertAsync(TeamSubscription subscription, CancellationToken ct = default)
    {
        var response = await _container.UpsertItemAsync(
            subscription,
            new PartitionKey(subscription.EntraIdGroup),
            cancellationToken: ct);
        return response.Resource;
    }

    public async Task<TeamSubscription?> GetAsync(string id, string entraIdGroup, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<TeamSubscription>(
                id,
                new PartitionKey(entraIdGroup),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TeamSubscription>> GetByGroupsAsync(IEnumerable<string> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<TeamSubscription>();
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE ARRAY_CONTAINS(@groups, c.entraIdGroup)")
            .WithParameter("@groups", ids);

        var results = new List<TeamSubscription>();
        using var iterator = _container.GetItemQueryIterator<TeamSubscription>(query);
        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(ct))
            {
                results.Add(item);
            }
        }

        return results;
    }

    public async Task DeleteAsync(string id, string entraIdGroup, CancellationToken ct = default)
    {
        try
        {
            await _container.DeleteItemAsync<TeamSubscription>(
                id,
                new PartitionKey(entraIdGroup),
                cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone; treat as success.
        }
    }
}
