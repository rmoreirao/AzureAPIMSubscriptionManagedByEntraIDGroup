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

    return fetch(`${base}${path}`, {...init, headers})
  }
}

export type ApiFetch = ReturnType<typeof createApiClient>
