using Microsoft.Azure.Cosmos;
using GroupSubscriptions.Functions.Models;

namespace GroupSubscriptions.Functions.Services;

public sealed class CosmosRepository
{
    private readonly Container _container;

    public CosmosRepository(CosmosClient client, CosmosOptions options)
    {
        _container = client.GetContainer(options.Database, options.Container);
    }

    public async Task<GroupSubscription> UpsertAsync(GroupSubscription subscription, CancellationToken ct = default)
    {
        var response = await _container.UpsertItemAsync(
            subscription,
            new PartitionKey(subscription.EntraIdGroup),
            cancellationToken: ct);
        return response.Resource;
    }

    public async Task<GroupSubscription?> GetAsync(string id, string entraIdGroup, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<GroupSubscription>(
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

    public async Task<IReadOnlyList<GroupSubscription>> GetByGroupsAsync(IEnumerable<string> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<GroupSubscription>();
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE ARRAY_CONTAINS(@groups, c.entraIdGroup)")
            .WithParameter("@groups", ids);

        var results = new List<GroupSubscription>();
        using var iterator = _container.GetItemQueryIterator<GroupSubscription>(query);
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
            await _container.DeleteItemAsync<GroupSubscription>(
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
