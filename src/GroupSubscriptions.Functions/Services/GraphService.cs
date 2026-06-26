using Microsoft.Graph;
using Microsoft.Graph.Models;
using GroupSubscriptions.Functions.Models;

namespace GroupSubscriptions.Functions.Services;

public sealed class GraphService
{
    private readonly GraphServiceClient _graph;

    public GraphService(GraphServiceClient graph)
    {
        _graph = graph;
    }

    /// <summary>Returns the Entra ID security groups the given user is a member of.</summary>
    public async Task<IReadOnlyList<EntraGroup>> GetUserGroupsAsync(string userId, CancellationToken ct = default)
    {
        var groups = new List<EntraGroup>();

        var page = await _graph.Users[userId].MemberOf.GraphGroup.GetAsync(req =>
        {
            req.QueryParameters.Select = new[] { "id", "displayName" };
            req.QueryParameters.Top = 100;
        }, ct);

        if (page?.Value is null)
        {
            return groups;
        }

        var iterator = Microsoft.Graph.PageIterator<Group, GroupCollectionResponse>
            .CreatePageIterator(_graph, page, group =>
            {
                groups.Add(new EntraGroup
                {
                    Id = group.Id ?? string.Empty,
                    DisplayName = group.DisplayName ?? string.Empty
                });
                return true;
            });

        await iterator.IterateAsync(ct);
        return groups;
    }

    /// <summary>Returns true when the user is a direct member of the given Entra ID group.</summary>
    public async Task<bool> IsMemberOfGroupAsync(string userId, string groupId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }

        var groups = await GetUserGroupsAsync(userId, ct);
        return groups.Any(g => g.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase));
    }
}
