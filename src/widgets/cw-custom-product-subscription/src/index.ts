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

type GroupSubscription = {
  subscriptionName: string
  entraIdGroup?: string | null
  groupName?: string | null
}

type SubscriptionType = "User" | "Group"

/** Normalized row used to render the Active subscriptions table. */
type SubscriptionRow = {
  type: SubscriptionType
  group: string
  name: string
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


  // --- Create form: configurable labels ---
  setText("createHeading", values.createHeading)
  setText("typeLabel", values.typeLabel)
  setText("nameLabel", values.nameLabel)
  setText("groupFieldLabel", values.groupFieldLabel)

  const personalOption = document.getElementById("typePersonalOption")
  if (personalOption) personalOption.textContent = values.typePersonalLabel
  const groupOption = document.getElementById("typeGroupOption")
  if (groupOption) groupOption.textContent = values.typeGroupLabel

  const nameInput = document.getElementById("subName") as HTMLInputElement | null
  if (nameInput) nameInput.placeholder = values.namePlaceholder

  const submitButton = document.getElementById("createSubmit") as HTMLButtonElement | null
  if (submitButton) submitButton.textContent = values.personalButtonLabel

  // Load the caller's existing subscriptions (user + group) into the top table.
  void refreshSubscriptions(apiFetch)

  let groupsLoaded = false

  const typeSelect = document.getElementById("subType") as HTMLSelectElement | null
  typeSelect?.addEventListener("change", () => {
    const isGroup = typeSelect.value === "group"
    if (isGroup) {
      show("groupRow")
    } else {
      hide("groupRow")
    }
    if (submitButton) {
      submitButton.textContent = isGroup ? values.groupButtonLabel : values.personalButtonLabel
    }
    setStatus("createStatus", "")
    if (isGroup && !groupsLoaded) {
      groupsLoaded = true
      void loadGroups(apiFetch, secrets.userId ?? "")
    }
  })

  setupCreateForm(apiFetch, scope)
}

// ---------------------------------------------------------------------------
// Active subscriptions table: lists the caller's user + group subscriptions.
// ---------------------------------------------------------------------------
function setSubsStatus(message: string, kind: StatusKind = "info"): void {
  setStatus("subsStatus", message, kind)
}

async function loadSubscriptionType(
  apiFetch: ApiFetch,
  label: SubscriptionType,
  path: string
): Promise<SubscriptionRow[]> {
  logInfo(`GET ${path}`)
  const response = await apiFetch(path)
  logInfo(`GET ${path} response`, {status: response.status, ok: response.ok})
  if (!response.ok) {
    const body = await readBodySafe(response)
    throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
  }
  const items = await response.json()
  if (!Array.isArray(items)) {
    logError(`${label} response was not an array`, items)
    return []
  }
  if (label === "Group") {
    return (items as GroupSubscription[]).map(item => ({
      type: "Group",
      group: item.groupName || item.entraIdGroup || "—",
      name: item.subscriptionName,
    }))
  }
  return (items as UserSubscription[]).map(item => ({
    type: "User",
    group: "—",
    name: item.subscriptionName,
  }))
}

function renderSubscriptions(rows: SubscriptionRow[]): void {
  const body = document.getElementById("subsBody")
  const count = document.getElementById("subsCount")
  const empty = document.getElementById("subsEmpty")
  if (!body) return

  body.innerHTML = ""
  for (const row of rows) {
    const tr = document.createElement("tr")

    const typeCell = document.createElement("td")
    const pill = document.createElement("span")
    pill.className = `pill pill--${row.type.toLowerCase()}`
    pill.textContent = row.type
    typeCell.appendChild(pill)

    const groupCell = document.createElement("td")
    groupCell.textContent = row.group || "—"

    const nameCell = document.createElement("td")
    nameCell.textContent = row.name

    tr.append(typeCell, groupCell, nameCell)
    body.appendChild(tr)
  }

  if (count) count.textContent = rows.length ? `${rows.length} total` : ""
  if (empty) empty.hidden = rows.length > 0
}

async function refreshSubscriptions(apiFetch: ApiFetch): Promise<void> {
  setSubsStatus("Loading your subscriptions…")
  const [groupResult, userResult] = await Promise.allSettled([
    loadSubscriptionType(apiFetch, "Group", "/apim/group-subscriptions"),
    loadSubscriptionType(apiFetch, "User", "/user-subscriptions"),
  ])

  const rows: SubscriptionRow[] = []
  if (groupResult.status === "fulfilled") rows.push(...groupResult.value)
  if (userResult.status === "fulfilled") rows.push(...userResult.value)

  const groupFailed = groupResult.status === "rejected"
  const userFailed = userResult.status === "rejected"

  if (groupFailed && userFailed) {
    logError("Both subscription sources failed", {group: groupResult, user: userResult})
    renderSubscriptions([])
    const empty = document.getElementById("subsEmpty")
    if (empty) empty.hidden = true
    setSubsStatus("Failed to load your subscriptions.", "error")
    return
  }

  renderSubscriptions(rows)

  if (groupFailed || userFailed) {
    const failed = groupFailed ? "group" : "user"
    setSubsStatus(`Loaded subscriptions, but the ${failed} subscriptions could not be loaded.`, "error")
  } else {
    setSubsStatus("")
  }
}

// ---------------------------------------------------------------------------
// Group loading: populate the APIM group dropdown for the current user.
// ---------------------------------------------------------------------------
async function loadGroups(apiFetch: ApiFetch, userId: string): Promise<void> {
  const groupSelect = document.getElementById("subGroup") as HTMLSelectElement | null

  if (!userId) {
    setStatus("createStatus", "Unable to determine the current user.", "error")
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
        setStatus("createStatus", "You are not a member of any APIM group.", "error")
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
    setStatus("createStatus", `Failed to load your APIM groups: ${describeError(error)}`, "error")
  }
}

// ---------------------------------------------------------------------------
// Create form: single inline bar handling both user and group subscriptions.
// ---------------------------------------------------------------------------
function setupCreateForm(apiFetch: ApiFetch, scope: string): void {
  const form = document.getElementById("createForm") as HTMLFormElement | null
  const typeSelect = document.getElementById("subType") as HTMLSelectElement | null
  const nameInput = document.getElementById("subName") as HTMLInputElement | null
  const groupSelect = document.getElementById("subGroup") as HTMLSelectElement | null
  const submitButton = document.getElementById("createSubmit") as HTMLButtonElement | null

  form?.addEventListener("submit", async event => {
    event.preventDefault()
    const subscriptionName = nameInput?.value.trim()
    if (!subscriptionName) {
      setStatus("createStatus", "Please provide a subscription name.", "error")
      return
    }

    const isGroup = typeSelect?.value === "group"
    const entraIdGroup = groupSelect?.value
    if (isGroup && !entraIdGroup) {
      setStatus("createStatus", "Please select an APIM group.", "error")
      return
    }

    if (submitButton) submitButton.disabled = true
    setStatus("createStatus", "Creating subscription…")
    try {
      if (isGroup) {
        const groupName = groupSelect?.selectedOptions[0]?.textContent ?? ""
        logInfo("POST /apim/group-subscriptions", {subscriptionName, entraIdGroup, groupName, scope})
        const response = await apiFetch("/apim/group-subscriptions", {
          method: "POST",
          headers: {"Content-Type": "application/json"},
          body: JSON.stringify({subscriptionName, entraIdGroup, groupName, scope}),
        })
        logInfo("POST /apim/group-subscriptions response", {status: response.status, ok: response.ok})
        if (!response.ok) {
          const body = await readBodySafe(response)
          throw new Error(`HTTP ${response.status} ${response.statusText} — ${body}`)
        }
      } else {
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
      }
      setStatus("createStatus", "Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
      await refreshSubscriptions(apiFetch)
    } catch (error) {
      logError("Failed to create subscription", error)
      setStatus("createStatus", `Failed to create the subscription: ${describeError(error)}`, "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

main()
