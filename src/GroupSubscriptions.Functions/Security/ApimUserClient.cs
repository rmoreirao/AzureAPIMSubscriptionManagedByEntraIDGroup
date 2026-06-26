using System.Net;
using Microsoft.Extensions.Logging;

namespace GroupSubscriptions.Functions.Security;

/// <summary>
/// Calls the APIM <b>management</b> REST API with the caller's SAS delegation token to prove the
/// token is genuinely valid for the claimed user. Because only APIM can mint/validate a Dev Portal
/// SAS token, a successful <c>GET /users/{userId}</c> is strong proof the request originates from a
/// real, logged-in Dev Portal user.
/// </summary>
public sealed class ApimUserClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ApimUserClient> _logger;

    public ApimUserClient(HttpClient http, ILogger<ApimUserClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns true when the APIM management API confirms the user exists and the SAS token is valid.
    /// </summary>
    public async Task<bool> UserExistsAsync(InvocationContext context, CancellationToken ct = default)
    {
        var baseUrl = context.ManagementApiUrl.TrimEnd('/');
        var requestUri = $"{baseUrl}/users/{Uri.EscapeDataString(context.UserId)}?api-version={Uri.EscapeDataString(context.ApiVersion)}";

        using var message = new HttpRequestMessage(HttpMethod.Get, requestUri);
        // The SAS scheme ("SharedAccessSignature token=...") is not a valid typed auth header, so add it raw.
        message.Headers.TryAddWithoutValidation("Authorization", context.Token);

        try
        {
            using var response = await _http.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }

            _logger.LogWarning(
                "APIM management user lookup returned {StatusCode} for user {UserId}",
                (int)response.StatusCode,
                context.UserId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "APIM management user lookup failed for user {UserId}", context.UserId);
            return false;
        }
    }
}
