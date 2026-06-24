using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using TeamSubscriptions.Functions.Models;
using TeamSubscriptions.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        IConfiguration config = context.Configuration;

        // Single keyless credential reused for every Azure service-to-service call.
        TokenCredential credential = new DefaultAzureCredential();
        services.AddSingleton(credential);

        var cosmosOptions = new CosmosOptions
        {
            Endpoint = config["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Cosmos:Endpoint is not configured."),
            Database = config["Cosmos:Database"] ?? "teamsubscriptions",
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
        services.AddSingleton<ApimManagementService>();
    })
    .Build();

host.Run();
