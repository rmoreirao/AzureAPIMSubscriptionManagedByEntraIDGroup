using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using TeamSubscriptions.Functions.Models;

namespace TeamSubscriptions.Functions.Security;

/// <summary>
/// Adds CORS headers to every HTTP response so the custom Dev Portal widgets can call the
/// Functions from the browser.
///
/// <para>
/// The platform-managed CORS setting (<c>siteConfig.cors</c>) is <b>ignored on the Flex
/// Consumption plan</b>, so CORS must be handled in code. Only the configured Dev Portal origin
/// (<see cref="DevPortalOptions.Url"/>) is allowed; the request <c>Origin</c> header is echoed back
/// when it matches.
/// </para>
/// </summary>
public sealed class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private readonly string _allowedOrigin;

    public CorsMiddleware(DevPortalOptions devPortal)
    {
        _allowedOrigin = Normalize(devPortal.Url);
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();

        await next(context);

        if (request is null)
        {
            return;
        }

        var response = context.GetHttpResponseData();
        if (response is null)
        {
            return;
        }

        AddCorsHeaders(request, response);
    }

    private void AddCorsHeaders(HttpRequestData request, HttpResponseData response)
    {
        var origin = GetHeader(request, "Origin");

        // Not a cross-origin browser request, or an origin we do not trust: add nothing.
        if (string.IsNullOrEmpty(origin) || !IsAllowed(origin))
        {
            return;
        }

        response.Headers.Remove("Access-Control-Allow-Origin");
        response.Headers.Add("Access-Control-Allow-Origin", origin);
        response.Headers.Add("Vary", "Origin");

        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");

            // Echo the headers the browser asked for; fall back to the set the widgets send.
            var requested = GetHeader(request, "Access-Control-Request-Headers");
            response.Headers.Add(
                "Access-Control-Allow-Headers",
                string.IsNullOrEmpty(requested)
                    ? "Authorization, Content-Type, xmh-userId, xmh-apiVersion, xmh-hostName, xmh-managementApiUrl, xmh-origin"
                    : requested);

            response.Headers.Add("Access-Control-Max-Age", "86400");
        }
    }

    private bool IsAllowed(string origin)
    {
        return !string.IsNullOrEmpty(_allowedOrigin)
            && Normalize(origin).Equals(_allowedOrigin, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? url)
    {
        return (url ?? string.Empty).Trim().TrimEnd('/');
    }

    private static string? GetHeader(HttpRequestData request, string name)
    {
        return request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
