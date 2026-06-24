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

    // Caches resolved product display names (keyed by product id) to avoid repeat control-plane calls
    // when many subscriptions share the same product within a single list request.
    private readonly Dictionary<string, string> _productNameCache = new(StringComparer.OrdinalIgnoreCase);

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

            var scope = data.Scope ?? string.Empty;
            var keys = await TryGetSecretsAsync(subscription, ct);

            views.Add(new UserSubscriptionView
            {
                SubscriptionId = data.Name,
                SubscriptionName = data.DisplayName ?? data.Name,
                State = data.State?.ToString() ?? string.Empty,
                Scope = scope,
                Product = await ResolveProductNameAsync(scope, ct),
                DateCreated = data.CreatedOn,
                PrimaryKey = keys.PrimaryKey,
                SecondaryKey = keys.SecondaryKey
            });
        }

        return views;
    }

    /// <summary>
    /// Verifies that the given subscription is owned by the supplied Dev Portal user. Used to authorize
    /// manage actions (regenerate/cancel) on user-owned subscriptions.
    /// </summary>
    public async Task<bool> IsOwnedByUserAsync(string subscriptionId, string userId, CancellationToken ct = default)
    {
        try
        {
            var subscription = await GetSubscriptionAsync(subscriptionId, ct);
            return string.Equals(subscription.Data.OwnerId, $"/users/{userId}", StringComparison.OrdinalIgnoreCase);
        }
        catch (RequestFailedException)
        {
            return false;
        }
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

    /// <summary>
    /// Fetches the live state, scope (resolved to a friendly product name) and keys for a subscription
    /// in a single call. Tolerant of a missing/inaccessible subscription (returns empty values), so the
    /// list endpoints never fail just because one APIM record can't be read.
    /// </summary>
    public async Task<SubscriptionDetails> GetSubscriptionDetailsAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var subscription = await GetSubscriptionAsync(subscriptionId, ct);
            var data = subscription.Data;
            var scope = data.Scope ?? string.Empty;

            var details = new SubscriptionDetails
            {
                State = data.State?.ToString() ?? string.Empty,
                Scope = scope,
                Product = await ResolveProductNameAsync(scope, ct)
            };

            var secrets = await TryGetSecretsAsync(subscription, ct);
            details.PrimaryKey = secrets.PrimaryKey;
            details.SecondaryKey = secrets.SecondaryKey;
            return details;
        }
        catch (RequestFailedException)
        {
            return new SubscriptionDetails();
        }
    }

    private static async Task<SubscriptionKeys> TryGetSecretsAsync(ApiManagementSubscriptionResource subscription, CancellationToken ct)
    {
        try
        {
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

    /// <summary>
    /// Resolves an APIM subscription scope to a friendly product/display name:
    /// <c>/apis</c> (all APIs) → "All APIs"; <c>.../products/{id}</c> → the product's display name;
    /// anything else falls back to the raw scope.
    /// </summary>
    private async Task<string> ResolveProductNameAsync(string scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return string.Empty;
        }

        var trimmed = scope.TrimEnd('/');

        // Whole-service / all-APIs scope (e.g. "/apis").
        if (trimmed.EndsWith("/apis", StringComparison.OrdinalIgnoreCase))
        {
            return "All APIs";
        }

        var productMarker = "/products/";
        var index = trimmed.IndexOf(productMarker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var productId = trimmed[(index + productMarker.Length)..].Split('/')[0];
            if (!string.IsNullOrWhiteSpace(productId))
            {
                return await GetProductDisplayNameAsync(productId, ct);
            }
        }

        // Unknown scope shape — surface the raw value rather than nothing.
        return scope;
    }

    private async Task<string> GetProductDisplayNameAsync(string productId, CancellationToken ct)
    {
        if (_productNameCache.TryGetValue(productId, out var cached))
        {
            return cached;
        }

        string display = productId;
        try
        {
            var id = ApiManagementProductResource.CreateResourceIdentifier(
                _options.SubscriptionId,
                _options.ResourceGroup,
                _options.ServiceName,
                productId);
            var product = await _arm.GetApiManagementProductResource(id).GetAsync(cancellationToken: ct);
            display = product.Value.Data.DisplayName ?? productId;
        }
        catch (RequestFailedException)
        {
            // Product not found / inaccessible — fall back to the id.
        }

        _productNameCache[productId] = display;
        return display;
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
