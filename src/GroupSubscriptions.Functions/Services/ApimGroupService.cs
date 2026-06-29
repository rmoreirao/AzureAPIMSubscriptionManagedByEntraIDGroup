using Azure.ResourceManager;
using Azure.ResourceManager.ApiManagement;
using Azure.ResourceManager.ApiManagement.Models;
using GroupSubscriptions.Functions.Models;

namespace GroupSubscriptions.Functions.Services;

/// <summary>
/// Resolves group membership from the <b>APIM Groups</b> (built-in + custom) that the Dev Portal
/// uses, rather than from Entra ID. Reads the APIM control
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

    /// <summary>
    /// Returns the <b>custom</b> APIM groups the given Dev Portal user is a member of. Built-in
    /// <c>system</c> groups (Administrators/Developers/Guests) and <c>external</c> groups are excluded:
    /// only custom groups (e.g. Team1/Team2/Team3) are eligible for group subscriptions. Because
    /// <see cref="IsMemberOfGroupAsync"/> and the list endpoints all use this method, the custom-only
    /// rule is enforced both when returning available groups and on subscription creation.
    /// </summary>
    public async Task<IReadOnlyList<EntraGroup>> GetUserGroupsAsync(string userId, CancellationToken ct = default)
    {
        var groups = new List<EntraGroup>();
        var user = GetUser(userId);

        await foreach (var group in user.GetUserGroupsAsync(cancellationToken: ct))
        {
            if (group.Data.GroupType != ApiManagementGroupType.Custom)
            {
                continue;
            }

            groups.Add(new EntraGroup
            {
                Id = group.Data.Name ?? string.Empty,
                DisplayName = group.Data.DisplayName ?? group.Data.Name ?? string.Empty
            });
        }

        return groups;
    }

    /// <summary>
    /// Returns true when the user is a member of the given APIM group. Only <b>custom</b> groups are
    /// considered (see <see cref="GetUserGroupsAsync"/>), so a request targeting a built-in
    /// <c>system</c> group resolves to false even if the user belongs to it.
    /// </summary>
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
