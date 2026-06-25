import {Secrets} from "@azure/api-management-custom-widgets-tools"

/**
 * Builds an authenticated fetch wrapper that calls the Team Subscriptions Azure Functions directly.
 *
 * The Dev Portal `secrets` provide the user's APIM delegation SAS token plus context, which we
 * forward as the `xmh-*` headers the Functions validate to confirm the caller is a logged-in
 * Dev Portal user.
 */
export function createApiClient(secrets: Secrets, functionBaseUrl: string) {
  const base = functionBaseUrl.replace(/\/+$/, "")

  return async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
    const headers = new Headers(init.headers || {})
    if (secrets.token) headers.set("Authorization", secrets.token)
    if (secrets.userId) headers.set("xmh-userId", secrets.userId)
    headers.set("xmh-managementApiUrl", secrets.managementApiUrl)
    headers.set("xmh-apiVersion", secrets.apiVersion)
    headers.set("xmh-origin", secrets.parentLocation.origin)
    headers.set("xmh-hostName", secrets.parentLocation.hostname)

    const method = init.method ?? "GET"
    const url = `${base}${path}`
    console.info(`[cw-team-subscriptions] ${method} ${url}`)
    try {
      const response = await fetch(url, {...init, headers})
      console.info(`[cw-team-subscriptions] ${method} ${url} → ${response.status} ${response.statusText}`)
      return response
    } catch (error) {
      // A malformed base URL (e.g. the unconfigured placeholder "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api")
      // makes fetch throw synchronously with no entry in the Network tab. Surface it clearly.
      console.error(
        `[cw-team-subscriptions] fetch failed for ${method} ${url}. ` +
          "Check the widget's 'functionBaseUrl' editor value and the Function App CORS settings.",
        error
      )
      throw error
    }
  }
}

export type ApiFetch = ReturnType<typeof createApiClient>
