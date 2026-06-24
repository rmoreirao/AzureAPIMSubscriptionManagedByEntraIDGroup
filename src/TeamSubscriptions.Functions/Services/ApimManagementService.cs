using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.ApiManagement;
using Azure.ResourceManager.ApiManagement.Models;
using TeamSubscriptions.Functions.Models;

namespace TeamSubscriptions.Functions.Services;

public sealed class ApimManagementService
{
    private readonly ArmClient _arm;
    private readonly ApimOptions _options;

    public ApimManagementService(ArmClient arm, ApimOptions options)
    {
        _arm = arm;
        _options = options;
    }

    private ApiManagementServiceResource GetService()
    {
        var id = ApiManagementServiceResource.CreateResourceIdentifier(
            _options.SubscriptionId,
            _options.ResourceGroup,
            _options.ServiceName);
        return _arm.GetApiManagementServiceResource(id);
    }

    /// <summary>Creates a standalone APIM subscription and returns its id + keys.</summary>
    public async Task<(string SubscriptionId, SubscriptionKeys Keys)> CreateSubscriptionAsync(
        string subscriptionId,
        string displayName,
        string scope,
        CancellationToken ct = default)
    {
        var service = GetService();
        var collection = service.GetApiManagementSubscriptions();

        var content = new ApiManagementSubscriptionCreateOrUpdateContent
        {
            DisplayName = displayName,
            Scope = scope,
            State = SubscriptionState.Active
        };

        var operation = await collection.CreateOrUpdateAsync(WaitUntil.Completed, subscriptionId, content, cancellationToken: ct);
        var secrets = await operation.Value.GetSecretsAsync(cancellationToken: ct);

        return (operation.Value.Data.Name, new SubscriptionKeys
        {
            PrimaryKey = secrets.Value.PrimaryKey,
            SecondaryKey = secrets.Value.SecondaryKey
        });
    }

    /// <summary>Creates an APIM subscription owned by a specific Dev Portal user and returns its id + keys.</summary>
    public async Task<(string SubscriptionId, SubscriptionKeys Keys)> CreateUserSubscriptionAsync(
        string subscriptionId,
        string displayName,
        string userId,
        string scope,
        CancellationToken ct = default)
    {
        var service = GetService();
        var collection = service.GetApiManagementSubscriptions();

        var content = new ApiManagementSubscriptionCreateOrUpdateContent
        {
            DisplayName = displayName,
            Scope = scope,
            OwnerId = $"/users/{userId}",
            State = SubscriptionState.Active
        };

        var operation = await collection.CreateOrUpdateAsync(WaitUntil.Completed, subscriptionId, content, cancellationToken: ct);
        var secrets = await operation.Value.GetSecretsAsync(cancellationToken: ct);

        return (operation.Value.Data.Name, new SubscriptionKeys
        {
            PrimaryKey = secrets.Value.PrimaryKey,
            SecondaryKey = secrets.Value.SecondaryKey
        });
    }

    /// <summary>Lists the APIM subscriptions owned by a given Dev Portal user.</summary>
    public async Task<List<UserSubscriptionView>> ListUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var service = GetService();
        var collection = service.GetApiManagementSubscriptions();
        var ownerId = $"/users/{userId}";

        var views = new List<UserSubscriptionView>();
        await foreach (var subscription in collection.GetAllAsync(cancellationToken: ct))
        {
            var data = subscription.Data;
            if (!string.Equals(data.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            views.Add(new UserSubscriptionView
            {
                SubscriptionId = data.Name,
                SubscriptionName = data.DisplayName ?? data.Name,
                State = data.State?.ToString() ?? string.Empty,
                Scope = data.Scope ?? string.Empty,
                DateCreated = data.CreatedOn
            });
        }

        return views;
    }

    public async Task<SubscriptionKeys> RegenerateKeysAsync(string subscriptionId, CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, ct);
        await subscription.RegeneratePrimaryKeyAsync(ct);
        await subscription.RegenerateSecondaryKeyAsync(ct);
        var secrets = await subscription.GetSecretsAsync(cancellationToken: ct);
        return new SubscriptionKeys
        {
            PrimaryKey = secrets.Value.PrimaryKey,
            SecondaryKey = secrets.Value.SecondaryKey
        };
    }
    public async Task<SubscriptionKeys> GetKeysAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var subscription = await GetSubscriptionAsync(subscriptionId, ct);
            var secrets = await subscription.GetSecretsAsync(cancellationToken: ct);
            return new SubscriptionKeys
            {
                PrimaryKey = secrets.Value.PrimaryKey,
                SecondaryKey = secrets.Value.SecondaryKey
            };
        }
        catch (RequestFailedException)
        {
            return new SubscriptionKeys();
        }
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, ct);
        await subscription.DeleteAsync(WaitUntil.Completed, ifMatch: ETag.All, cancellationToken: ct);
    }

    private async Task<ApiManagementSubscriptionResource> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        var id = ApiManagementSubscriptionResource.CreateResourceIdentifier(
            _options.SubscriptionId,
            _options.ResourceGroup,
            _options.ServiceName,
            subscriptionId);
        var resource = _arm.GetApiManagementSubscriptionResource(id);
        return await resource.GetAsync(cancellationToken: ct);
    }
}
