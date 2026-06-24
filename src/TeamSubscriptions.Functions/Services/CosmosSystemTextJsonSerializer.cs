using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace TeamSubscriptions.Functions.Services;

/// <summary>
/// Cosmos serializer backed by System.Text.Json so that [JsonPropertyName]
/// attributes on models are honored (the SDK default uses Newtonsoft, which
/// ignores them and causes the required lowercase "id" field to be missing).
/// </summary>
public sealed class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _options;

    public CosmosSystemTextJsonSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
            {
                return default!;
            }

            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return JsonSerializer.Deserialize<T>(stream, _options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, input, _options);
        stream.Position = 0;
        return stream;
    }
}
