using System.Text.Json;
using Azure.Core;
using Azure.Core.Serialization;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using GroupSubscriptions.Functions.Models;
using GroupSubscriptions.Functions.Security;
using GroupSubscriptions.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        // Flex Consumption ignores the platform CORS setting, so CORS is handled in code.
        worker.UseMiddleware<CorsMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        IConfiguration config = context.Configuration;

        // Serialize HTTP responses as camelCase (Web defaults) so the custom Dev Portal widgets,
        // which read camelCase fields (e.g. subscriptionName, state, primaryKey), bind correctly.
        // Reads stay case-insensitive, so the camelCase request bodies the widgets send still bind.
        services.Configure<WorkerOptions>(options =>
        {
            options.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        });

        // Single keyless credential reused for every Azure service-to-service call.
        TokenCredential credential = new DefaultAzureCredential();
        services.AddSingleton(credential);

        var cosmosOptions = new CosmosOptions
        {
            Endpoint = config["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Cosmos:Endpoint is not configured."),
            Database = config["Cosmos:Database"] ?? "groupsubscriptions",
            Container = config["Cosmos:Container"] ?? "subscriptions"
        };
        services.AddSingleton(cosmosOptions);

        var apimOptions = new ApimOptions
        {
            SubscriptionId = config["Apim:SubscriptionId"] ?? throw new InvalidOperationException("Apim:SubscriptionId is not configured."),
            ResourceGroup = config["Apim:ResourceGroup"] ?? throw new InvalidOperationException("Apim:ResourceGroup is not configured."),
            ServiceName = config["Apim:ServiceName"] ?? throw new InvalidOperationException("Apim:ServiceName is not configured.")
        };
        services.AddSingleton(apimOptions);

        var devPortalOptions = new DevPortalOptions
        {
            Url = config["DevPortal:Url"] ?? throw new InvalidOperationException("DevPortal:Url is not configured.")
        };
        services.AddSingleton(devPortalOptions);

        // Keyless Cosmos DB client (RBAC data-plane via managed identity).
        // Use System.Text.Json so [JsonPropertyName] attributes (e.g. "id") are honored;
        // the SDK default (Newtonsoft) ignores them and Cosmos rejects the document.
        services.AddSingleton(sp => new CosmosClient(cosmosOptions.Endpoint, credential, new CosmosClientOptions
        {
            Serializer = new CosmosSystemTextJsonSerializer(System.Text.Json.JsonSerializerOptions.Default)
        }));

        // Keyless Microsoft Graph client (managed identity with Graph app permissions).
        services.AddSingleton(sp => new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" }));

        // Keyless ARM client to manage APIM subscriptions.
        services.AddSingleton(sp => new ArmClient(credential));

        services.AddSingleton<CosmosRepository>();
        services.AddSingleton<GraphService>();
        services.AddSingleton<ApimGroupService>();
        services.AddSingleton<ApimManagementService>();

        // Dev Portal request authentication (validates the caller is a logged-in APIM Dev Portal user).
        services.AddHttpClient<ApimUserClient>();
        services.AddSingleton<RequestAuthService>();
    })
    .Build();

host.Run();
