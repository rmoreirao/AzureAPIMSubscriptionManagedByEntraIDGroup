using Microsoft.Extensions.Logging;
using TeamSubscriptions.Functions.Models;

namespace TeamSubscriptions.Functions.Security;

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public sealed class AuthResult
{
    private AuthResult(bool isAuthorized, string userId)
    {
        IsAuthorized = isAuthorized;
        UserId = userId;
    }

    public bool IsAuthorized { get; }

    /// <summary>The validated Dev Portal user id (empty when unauthorized).</summary>
    public string UserId { get; }

    public static AuthResult Authorized(string userId) => new(true, userId);

    public static AuthResult Unauthorized() => new(false, string.Empty);
}

/// <summary>
/// Validates that an incoming request originates from a valid, logged-in APIM Dev Portal user.
/// Performs the full check:
/// <list type="number">
///   <item>all required <c>xmh-*</c> + SAS headers are present;</item>
///   <item>the SAS token belongs to the claimed user;</item>
///   <item>the request origin is the configured Dev Portal;</item>
///   <item>the APIM management API confirms the user/token (only APIM can validate a SAS token).</item>
/// </list>
/// </summary>
public sealed class RequestAuthService
{
    private readonly ApimUserClient _apimUserClient;
    private readonly DevPortalOptions _options;
    private readonly ILogger<RequestAuthService> _logger;

    public RequestAuthService(ApimUserClient apimUserClient, DevPortalOptions options, ILogger<RequestAuthService> logger)
    {
        _apimUserClient = apimUserClient;
        _options = options;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(InvocationContext context, CancellationToken ct = default)
    {
        if (!context.HasRequiredHeaders)
        {
            _logger.LogWarning("Unauthorized: missing one or more required authentication headers.");
            return AuthResult.Unauthorized();
        }

        // The SAS token must be issued for the same user that the request claims to act as.
        var expectedTokenStart = $"SharedAccessSignature token=\"{context.UserId}";
        if (!context.Token.StartsWith(expectedTokenStart, StringComparison.Ordinal))
        {
            _logger.LogWarning("Unauthorized: SAS token does not match the claimed user {UserId}.", context.UserId);
            return AuthResult.Unauthorized();
        }

        // The request must come from the configured Dev Portal origin.
        if (string.IsNullOrWhiteSpace(_options.Url) ||
            context.Origin.IndexOf(_options.Url, StringComparison.OrdinalIgnoreCase) < 0)
        {
            _logger.LogWarning(
                "Unauthorized: origin '{Origin}' does not match the configured Dev Portal URL.",
                context.Origin);
            return AuthResult.Unauthorized();
        }

        // Final proof: APIM must confirm the SAS token is valid for this user.
        if (!await _apimUserClient.UserExistsAsync(context, ct))
        {
            _logger.LogWarning("Unauthorized: APIM could not validate user {UserId}.", context.UserId);
            return AuthResult.Unauthorized();
        }

        return AuthResult.Authorized(context.UserId);
    }
}
