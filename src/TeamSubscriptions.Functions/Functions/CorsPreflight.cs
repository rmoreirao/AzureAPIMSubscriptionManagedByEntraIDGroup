using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TeamSubscriptions.Functions.Functions;

/// <summary>
/// Catch-all handler for CORS preflight (<c>OPTIONS</c>) requests.
///
/// <para>
/// The other functions only declare <c>get</c>/<c>post</c>, so the Functions host would answer an
/// <c>OPTIONS</c> preflight with 404 and never reach the worker. This anonymous catch-all gives the
/// host a route to dispatch preflight requests to; <see cref="Security.CorsMiddleware"/> then
/// decorates the response with the required <c>Access-Control-*</c> headers.
/// </para>
/// </summary>
public sealed class CorsPreflight
{
    [Function("CorsPreflight")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*path}")] HttpRequestData req)
    {
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
