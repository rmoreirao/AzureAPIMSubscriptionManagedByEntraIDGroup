import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

type EntraGroup = {id: string; displayName: string}

type UserSubscription = {
  subscriptionId: string
  subscriptionName: string
  state: string
  scope: string
  dateCreated?: string
}

type StatusKind = "info" | "error" | "success"

const LOG_PREFIX = "[cw-custom-product-subscription]"

function logInfo(message: string, ...details: unknown[]): void {
  console.info(`${LOG_PREFIX} ${message}`, ...details)
}

function logError(message: string, error: unknown): void {
  console.error(`${LOG_PREFIX} ${message}`, error)
}

/** Returns a short, user-displayable description of an error. */
function describeError(error: unknown): string {
  if (error instanceof Error) return error.message
  return String(error)
}

/** Reads a response body for diagnostics without throwing. */
async function readBodySafe(response: Response): Promise<string> {
  try {
    return await response.text()
  } catch {
    return "<unreadable body>"
  }
}

/**
 * Derives the APIM subscription scope from the Dev Portal product page the widget is hosted on.
 *
 * The portal forwards its own `window.location` as `secrets.parentLocation`, and a product details
 * page is addressed via the hash fragment `#product={productId}` (e.g.
 * `/product#product=starter`). When a product id is present we scope the subscription to that
 * product (`/products/{productId}`); otherwise we fall back to the editor-configured `scope`
 * (default `/apis` = all APIs) so the widget still works on non-product pages.
 */
function resolveProductScope(href: string | undefined, fallbackScope: string): string {
  if (href) {
    const hashIndex = href.indexOf("#")
    if (hashIndex >= 0) {
      const productId = new URLSearchParams(href.slice(hashIndex + 1)).get("product")
      if (productId) return `/products/${productId}`
    }
  }
  return fallbackScope
}

/**
 * Returns true when the configured Function base URL looks usable. The default placeholder contains
 * `<...>` which makes `fetch` throw synchronously (no network request, no console error), so we
 * detect and report it explicitly.
 */
function isFunctionBaseUrlConfigured(functionBaseUrl: string): boolean {
  if (!functionBaseUrl) return false
  if (functionBaseUrl.includes("<") || functionBaseUrl.includes(">")) return false
  try {
    // eslint-disable-next-line no-new
    new URL(functionBaseUrl)
    return true
  } catch {
    return false
  }
}

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
}

function setStatus(id: string, message: string, kind: StatusKind = "info"): void {
  const el = document.getElementById(id)
  if (!el) return
  el.textContent = message
  el.className = `status status--${kind}`
}

function show(id: string): void {
  document.getElementById(id)?.removeAttribute("hidden")
}

function hide(id: string): void {
  document.getElementById(id)?.setAttribute("hidden", "")
}

async function main(): Promise<void> {
  const values = getValues(valuesDefault)
  const secrets = await askForSecrets("app")
  const apiFetch = createApiClient(secrets, values.functionBaseUrl)

  logInfo("Widget initialized", {
    functionBaseUrl: values.functionBaseUrl,
    scope: values.scope,
    userId: secrets.userId,
    hasToken: Boolean(secrets.token),
    managementApiUrl: secrets.managementApiUrl,
    origin: secrets.parentLocation?.origin,
  })

  const baseUrlConfigured = isFunctionBaseUrlConfigured(values.functionBaseUrl)
  if (!baseUrlConfigured) {
    logError(
      "Function base URL is not configured. Set the widget's 'functionBaseUrl' editor value to the " +
        "deployed Function App API base (e.g. https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api). The current value " +
        "is not a valid URL, so fetch() throws before any network request is made.",
      values.functionBaseUrl
    )
  }

  // Scope new subscriptions to the product whose Dev Portal page hosts this widget
  // (parentLocation hash `#product={id}` → `/products/{id}`), falling back to the editor value.
  const scope = resolveProductScope(secrets.parentLocation?.href, values.scope)
  logInfo("Resolved subscription scope", {parentHref: secrets.parentLocation?.href, scope})


  // --- Step 1: chooser labels ---
  setText("title", values.title)
  setText("userSubscriptionLabel", values.userSubscriptionLabel)
  setText("userSubscriptionDescription", values.userSubscriptionDescription)
  setText("teamSubscriptionLabel", values.teamSubscriptionLabel)
  setText("teamSubscriptionDescription", values.teamSubscriptionDescription)
  setText("userPanelTitle", values.userPanelTitle)
  setText("teamPanelTitle", values.teamPanelTitle)

  let userPanelLoaded = false
  let teamPanelLoaded = false

  function showChooser(): void {
    show("chooser")
    hide("userPanel")
    hide("teamPanel")
  }

  document.getElementById("userSubscription")?.addEventListener("click", () => {
    hide("chooser")
    hide("teamPanel")
    show("userPanel")
    if (!userPanelLoaded) {
      userPanelLoaded = true
      void loadUserSubscriptions(apiFetch)
    }
  })

  document.getElementById("teamSubscription")?.addEventListener("click", () => {
    hide("chooser")
    hide("userPanel")
    show("teamPanel")
    if (!teamPanelLoaded) {
      teamPanelLoaded = true
      void loadGroups(apiFetch, secrets.userId ?? "")
    }
  })

  document.querySelectorAll("[data-back]").forEach(button => {
    button.addEventListener("click", () => showChooser())
  })

  setupUserPanel(apiFetch, scope)
  setupTeamPanel(apiFetch, scope)
}

// ---------------------------------------------------------------------------
// User subscription panel: list existing subscriptions + create form.
// ---------------------------------------------------------------------------
async function loadUserSubscriptions(apiFetch: ApiFetch): Promise<void> {
  const list = document.getElementById("userSubList")
  if (!list) return
  list.innerHTML = `<p class="user-sub-empty">Loading your subscriptions…</p>`
  try {
    logInfo("GET /user-subscriptions")
    const response = await apiFetch("/user-subscriptions")
    logInfo("GET /user-subscriptions response", {status: response.status, ok: response.ok})
    if (!response.ok) {
      const body = await readBodySafe(response)
      throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
    }
    const subscriptions: UserSubscription[] = await response.json()
    logInfo("GET /user-subscriptions returned subscriptions", subscriptions)
    renderUserSubscriptions(list, subscriptions)
  } catch (error) {
    logError("Failed to load user subscriptions", error)
    list.innerHTML = `<p class="user-sub-empty">Failed to load your subscriptions.</p>`
  }
}

function renderUserSubscriptions(list: HTMLElement, subscriptions: UserSubscription[]): void {
  list.innerHTML = ""
  if (!subscriptions || subscriptions.length === 0) {
    const empty = document.createElement("p")
    empty.className = "user-sub-empty"
    empty.textContent = "You don't have subscriptions yet."
    list.appendChild(empty)
    return
  }

  for (const subscription of subscriptions) {
    const item = document.createElement("div")
    item.className = "user-sub-item"

    const name = document.createElement("span")
    name.className = "user-sub-item__name"
    name.textContent = subscription.subscriptionName || subscription.subscriptionId
    item.appendChild(name)

    const meta = document.createElement("span")
    meta.className = "user-sub-item__meta"
    const parts = [subscription.state, subscription.scope].filter(Boolean)
    meta.textContent = parts.join(" · ")
    item.appendChild(meta)

    list.appendChild(item)
  }
}

function setupUserPanel(apiFetch: ApiFetch, scope: string): void {
  const form = document.getElementById("userForm") as HTMLFormElement | null
  const nameInput = document.getElementById("userSubscriptionName") as HTMLInputElement | null
  const submitButton = document.getElementById("userSubmit") as HTMLButtonElement | null

  form?.addEventListener("submit", async event => {
    event.preventDefault()
    const subscriptionName = nameInput?.value.trim()
    if (!subscriptionName) {
      setStatus("userStatus", "Please provide a subscription name.", "error")
      return
    }

    if (submitButton) submitButton.disabled = true
    setStatus("userStatus", "Creating subscription…")
    try {
      logInfo("POST /user-subscriptions", {subscriptionName, scope})
      const response = await apiFetch("/user-subscriptions", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({subscriptionName, scope}),
      })
      logInfo("POST /user-subscriptions response", {status: response.status, ok: response.ok})
      if (!response.ok) {
        const body = await readBodySafe(response)
        throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
      }
      setStatus("userStatus", "Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
      await loadUserSubscriptions(apiFetch)
    } catch (error) {
      logError("Failed to create user subscription", error)
      setStatus("userStatus", `Failed to create the subscription: ${describeError(error)}`, "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

// ---------------------------------------------------------------------------
// Team subscription panel: pick an APIM group + create form.
// ---------------------------------------------------------------------------
async function loadGroups(apiFetch: ApiFetch, userId: string): Promise<void> {
  const groupSelect = document.getElementById("entraIdGroup") as HTMLSelectElement | null

  if (!userId) {
    setStatus("teamStatus", "Unable to determine the current user.", "error")
    return
  }

  try {
    logInfo("GET /apim/users/{userId}/groups", {userId})
    const response = await apiFetch(`/apim/users/${encodeURIComponent(userId)}/groups`)
    logInfo("GET groups response", {status: response.status, ok: response.ok})
    if (!response.ok) {
      const body = await readBodySafe(response)
      throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
    }
    const groups: EntraGroup[] = await response.json()
    if (groupSelect) {
      groupSelect.innerHTML = ""
      if (groups.length === 0) {
        setStatus("teamStatus", "You are not a member of any APIM group.", "error")
      }
      for (const group of groups) {
        const option = document.createElement("option")
        option.value = group.id
        option.textContent = group.displayName || group.id
        groupSelect.appendChild(option)
      }
    }
  } catch (error) {
    logError("Failed to load APIM groups", error)
    setStatus("teamStatus", `Failed to load your APIM groups: ${describeError(error)}`, "error")
  }
}

function setupTeamPanel(apiFetch: ApiFetch, scope: string): void {
  const form = document.getElementById("teamForm") as HTMLFormElement | null
  const nameInput = document.getElementById("teamSubscriptionName") as HTMLInputElement | null
  const groupSelect = document.getElementById("entraIdGroup") as HTMLSelectElement | null
  const submitButton = document.getElementById("teamSubmit") as HTMLButtonElement | null

  form?.addEventListener("submit", async event => {
    event.preventDefault()
    const subscriptionName = nameInput?.value.trim()
    const entraIdGroup = groupSelect?.value
    if (!subscriptionName || !entraIdGroup) {
      setStatus("teamStatus", "Please provide a subscription name and select a group.", "error")
      return
    }

    if (submitButton) submitButton.disabled = true
    setStatus("teamStatus", "Creating subscription…")
    try {
      const teamName = groupSelect?.selectedOptions[0]?.textContent ?? ""
      logInfo("POST /apim/team-subscriptions", {subscriptionName, entraIdGroup, teamName, scope})
      const response = await apiFetch("/apim/team-subscriptions", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({subscriptionName, entraIdGroup, teamName, scope}),
      })
      logInfo("POST /apim/team-subscriptions response", {status: response.status, ok: response.ok})
      if (!response.ok) {
        const body = await readBodySafe(response)
        throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
      }
      setStatus("teamStatus", "Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
    } catch (error) {
      logError("Failed to create team subscription", error)
      setStatus("teamStatus", `Failed to create the subscription: ${describeError(error)}`, "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

main()
