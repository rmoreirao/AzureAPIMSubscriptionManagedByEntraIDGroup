using Azure.ResourceManager;
using Azure.ResourceManager.ApiManagement;
using GroupSubscriptions.Functions.Models;

namespace GroupSubscriptions.Functions.Services;

/// <summary>
/// Resolves group membership from the <b>APIM Groups</b> (built-in + custom) that the Dev Portal
/// uses, rather than from Entra ID. Mirrors <see cref="GraphService"/> but reads the APIM control
/// plane via the shared <see cref="ArmClient"/>. The Function managed identity already has
/// <c>API Management Service Contributor</c>, so no extra RBAC/Graph permissions are required.
/// </summary>
public sealed class ApimGroupService
{
    private readonly ArmClient _arm;
    private readonly ApimOptions _options;

    public ApimGroupService(ArmClient arm, ApimOptions options)
    {
        _arm = arm;
        _options = options;
    }

    private ApiManagementUserResource GetUser(string userId)
    {
        var id = ApiManagementUserResource.CreateResourceIdentifier(
            _options.SubscriptionId,
            _options.ResourceGroup,
            _options.ServiceName,
            userId);
        return _arm.GetApiManagementUserResource(id);
    }

    /// <summary>Returns the APIM groups the given Dev Portal user is a member of.</summary>
    public async Task<IReadOnlyList<EntraGroup>> GetUserGroupsAsync(string userId, CancellationToken ct = default)
    {
        var groups = new List<EntraGroup>();
        var user = GetUser(userId);

        await foreach (var group in user.GetUserGroupsAsync(cancellationToken: ct))
        {
            groups.Add(new EntraGroup
            {
                Id = group.Data.Name ?? string.Empty,
                DisplayName = group.Data.DisplayName ?? group.Data.Name ?? string.Empty
            });
        }

        return groups;
    }

    /// <summary>Returns true when the user is a member of the given APIM group.</summary>
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
