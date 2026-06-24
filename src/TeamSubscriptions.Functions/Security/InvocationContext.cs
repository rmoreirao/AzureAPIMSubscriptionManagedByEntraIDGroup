namespace TeamSubscriptions.Functions.Security;

/// <summary>
/// Per-request authentication context built from the Dev Portal headers forwarded by a custom widget:
/// the user's APIM delegation SAS token plus the <c>xmh-*</c> context headers.
/// </summary>
public sealed class InvocationContext
{
    public InvocationContext(
        string? token,
        string? userId,
        string? apiVersion,
        string? hostname,
        string? managementApiUrl,
        string? origin)
    {
        Token = token ?? string.Empty;
        UserId = userId ?? string.Empty;
        ApiVersion = apiVersion ?? string.Empty;
        Hostname = hostname ?? string.Empty;
        ManagementApiUrl = managementApiUrl ?? string.Empty;
        Origin = origin ?? string.Empty;
    }

    /// <summary>The <c>Authorization</c> header value: <c>SharedAccessSignature token="..."</c>.</summary>
    public string Token { get; }

    /// <summary>The APIM Dev Portal user id (<c>xmh-userId</c>).</summary>
    public string UserId { get; }

    /// <summary>APIM management API version (<c>xmh-apiVersion</c>).</summary>
    public string ApiVersion { get; }

    /// <summary>Dev Portal hostname (<c>xmh-hostName</c>).</summary>
    public string Hostname { get; }

    /// <summary>Fully-qualified APIM management API resource URL (<c>xmh-managementApiUrl</c>).</summary>
    public string ManagementApiUrl { get; }

    /// <summary>Browser origin of the Dev Portal request (<c>xmh-origin</c>).</summary>
    public string Origin { get; }

    /// <summary>
    /// True when every header required to authenticate the request is present.
    /// </summary>
    public bool HasRequiredHeaders =>
        !string.IsNullOrWhiteSpace(Token) &&
        !string.IsNullOrWhiteSpace(UserId) &&
        !string.IsNullOrWhiteSpace(ApiVersion) &&
        !string.IsNullOrWhiteSpace(ManagementApiUrl) &&
        !string.IsNullOrWhiteSpace(Origin);
}
