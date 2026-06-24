using Microsoft.Azure.Functions.Worker.Http;

namespace TeamSubscriptions.Functions.Security;

/// <summary>
/// Builds an <see cref="InvocationContext"/> from the headers of an isolated-worker
/// <see cref="HttpRequestData"/>. Header reads are case-insensitive and never throw —
/// missing headers simply yield an empty value, which <see cref="RequestAuthService"/> treats
/// as an unauthorized request.
/// </summary>
public static class HttpRequestReader
{
    public static InvocationContext GetInvocationContext(HttpRequestData request)
    {
        return new InvocationContext(
            token: GetHeader(request, "Authorization"),
            userId: GetHeader(request, "xmh-userId"),
            apiVersion: GetHeader(request, "xmh-apiVersion"),
            hostname: GetHeader(request, "xmh-hostName"),
            managementApiUrl: GetHeader(request, "xmh-managementApiUrl"),
            origin: GetHeader(request, "xmh-origin"));
    }

    private static string? GetHeader(HttpRequestData request, string name)
    {
        if (request.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }
}
