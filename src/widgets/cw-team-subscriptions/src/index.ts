import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

const LOG = "[cw-team-subscriptions]"

type SubscriptionType = "User" | "Team"

/** Raw shape returned by GET /apim/team-subscriptions. */
type TeamSubscriptionView = {
  subscriptionId: string
  subscriptionName: string
  entraIdGroup: string
  teamName: string
  state?: string | null
  product?: string | null
  dateCreated: string
  primaryKey?: string | null
  secondaryKey?: string | null
}

/** Raw shape returned by GET /user-subscriptions. */
type UserSubscriptionView = {
  subscriptionId: string
  subscriptionName: string
  state?: string | null
  scope?: string | null
  product?: string | null
  dateCreated?: string | null
  primaryKey?: string | null
  secondaryKey?: string | null
}

/** Normalized row used to render the merged table. */
type SubscriptionRow = {
  type: SubscriptionType
  subscriptionId: string
  subscriptionName: string
  group: string
  entraIdGroup: string
  product: string
  state: string
  dateCreated: string
  primaryKey: string
  secondaryKey: string
}

const MASK = "••••••••••••••••"

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
}

function formatDate(value: string): string {
  if (!value) return ""
  const date = new Date(value)
  return isNaN(date.getTime()) ? value : date.toLocaleDateString()
}

function isActive(state: string): boolean {
  return state.toLowerCase() === "active"
}

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Partial<HTMLElementTagNameMap[K]> = {},
  children: (Node | string)[] = []
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag)
  Object.assign(node, props)
  for (const child of children) {
    node.append(child)
  }
  return node
}

function normalizeTeam(item: TeamSubscriptionView): SubscriptionRow {
  return {
    type: "Team",
    subscriptionId: item.subscriptionId,
    subscriptionName: item.subscriptionName,
    group: item.teamName || item.entraIdGroup || "",
    entraIdGroup: item.entraIdGroup || "",
    product: item.product || "",
    state: item.state || "",
    dateCreated: item.dateCreated || "",
    primaryKey: item.primaryKey || "",
    secondaryKey: item.secondaryKey || "",
  }
}

function normalizeUser(item: UserSubscriptionView): SubscriptionRow {
  return {
    type: "User",
    subscriptionId: item.subscriptionId,
    subscriptionName: item.subscriptionName,
    group: "",
    entraIdGroup: "",
    product: item.product || item.scope || "",
    state: item.state || "",
    dateCreated: item.dateCreated || "",
    primaryKey: item.primaryKey || "",
    secondaryKey: item.secondaryKey || "",
  }
}

async function main(): Promise<void> {
  const values = getValues(valuesDefault)
  const secrets = await askForSecrets("app")
  const apiFetch = createApiClient(secrets, values.functionBaseUrl)

  setText("title", values.title)

  const status = document.getElementById("status")
  const tableBody = document.getElementById("tableBody") as HTMLTableSectionElement | null
  const emptyState = document.getElementById("emptyState")

  // Tracks the kebab menu that is currently open so we can close it on an outside click.
  let openMenu: HTMLElement | null = null

  function closeMenu(): void {
    if (openMenu) {
      openMenu.hidden = true
      openMenu = null
    }
  }

  document.addEventListener("click", () => closeMenu())

  function setStatus(message: string, kind: "info" | "error" | "success" = "info"): void {
    if (!status) return
    status.textContent = message
    status.className = `status status--${kind}`
  }

  function actionBasePath(row: SubscriptionRow): string {
    return row.type === "Team"
      ? `/apim/team-subscriptions/${encodeURIComponent(row.entraIdGroup)}/${encodeURIComponent(row.subscriptionId)}`
      : `/user-subscriptions/${encodeURIComponent(row.subscriptionId)}`
  }

  function renderRow(row: SubscriptionRow): HTMLTableRowElement {
    const active = isActive(row.state)

    // Key cells (reveal state is controlled by the row's "Show keys" menu action).
    const primaryValue = el("span", {className: "key-value", textContent: row.primaryKey ? MASK : "—"})
    const secondaryValue = el("span", {className: "key-value", textContent: row.secondaryKey ? MASK : "—"})
    let revealed = false

    function setRevealed(next: boolean): void {
      revealed = next
      primaryValue.textContent = row.primaryKey ? (revealed ? row.primaryKey : MASK) : "—"
      secondaryValue.textContent = row.secondaryKey ? (revealed ? row.secondaryKey : MASK) : "—"
    }

    const tr = el("tr")
    tr.append(
      el("td", {textContent: row.subscriptionName}),
      el("td", {textContent: row.type}),
      el("td", {textContent: row.group || "—"}),
      el("td", {textContent: row.product || "—"})
    )

    const stateCell = el("td", {textContent: row.state || "—", className: active ? "state state--active" : "state state--inactive"})
    tr.append(stateCell)

    if (active) {
      tr.append(el("td", {}, [primaryValue]), el("td", {}, [secondaryValue]))
    } else {
      // Inactive subscriptions have no usable keys — show a single explanatory message.
      tr.append(el("td", {className: "inactive-note", colSpan: 2}, [el("em", {textContent: "The subscription is not active"})]))
    }

    tr.append(el("td", {textContent: formatDate(row.dateCreated)}))

    // Actions: a kebab menu, only offered for active subscriptions.
    const actionsCell = el("td", {className: "actions-col"})
    if (active) {
      const menu = el("div", {className: "menu", hidden: true})

      const showItem = el("button", {type: "button", className: "menu-item", textContent: "Show keys"})
      const hasKeys = Boolean(row.primaryKey || row.secondaryKey)
      showItem.disabled = !hasKeys
      showItem.addEventListener("click", (e) => {
        e.stopPropagation()
        setRevealed(!revealed)
        showItem.textContent = revealed ? "Hide keys" : "Show keys"
        closeMenu()
      })

      const regenItem = el("button", {type: "button", className: "menu-item", textContent: "Regenerate keys"})
      regenItem.addEventListener("click", async (e) => {
        e.stopPropagation()
        closeMenu()
        regenItem.disabled = true
        setStatus(`Regenerating keys for "${row.subscriptionName}"…`)
        try {
          const response = await apiFetch(`${actionBasePath(row)}/regenerate`, {method: "POST"})
          if (!response.ok) throw new Error(`HTTP ${response.status}`)
          const keys: {primaryKey?: string; secondaryKey?: string} = await response.json()
          row.primaryKey = keys.primaryKey || ""
          row.secondaryKey = keys.secondaryKey || ""
          setRevealed(revealed)
          setStatus(`Keys regenerated for "${row.subscriptionName}".`, "success")
        } catch (error) {
          console.error(`${LOG} failed to regenerate keys for "${row.subscriptionName}" (${row.subscriptionId})`, error)
          setStatus(`Failed to regenerate keys for "${row.subscriptionName}".`, "error")
        } finally {
          regenItem.disabled = false
        }
      })

      const cancelItem = el("button", {type: "button", className: "menu-item menu-item--danger", textContent: "Cancel subscription"})
      cancelItem.addEventListener("click", async (e) => {
        e.stopPropagation()
        closeMenu()
        if (!window.confirm(`Cancel subscription "${row.subscriptionName}"? This cannot be undone.`)) return
        cancelItem.disabled = true
        setStatus(`Cancelling "${row.subscriptionName}"…`)
        try {
          const response = await apiFetch(`${actionBasePath(row)}/cancel`, {method: "POST"})
          if (!response.ok) throw new Error(`HTTP ${response.status}`)
          tr.remove()
          setStatus(`Subscription "${row.subscriptionName}" cancelled.`, "success")
          if (tableBody && tableBody.childElementCount === 0 && emptyState) {
            emptyState.hidden = false
          }
        } catch (error) {
          console.error(`${LOG} failed to cancel "${row.subscriptionName}" (${row.subscriptionId})`, error)
          setStatus(`Failed to cancel "${row.subscriptionName}".`, "error")
          cancelItem.disabled = false
        }
      })

      menu.append(showItem, regenItem, cancelItem)

      const kebab = el("button", {type: "button", className: "kebab", textContent: "⋯"})
      kebab.setAttribute("aria-label", "Subscription actions")
      kebab.addEventListener("click", (e) => {
        e.stopPropagation()
        const willOpen = menu.hidden
        closeMenu()
        if (willOpen) {
          menu.hidden = false
          openMenu = menu
        }
      })

      actionsCell.append(kebab, menu)
    }
    tr.append(actionsCell)

    return tr
  }

  async function loadType(label: SubscriptionType, path: string): Promise<SubscriptionRow[]> {
    try {
      const response = await apiFetch(path)
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      const items = await response.json()
      if (!Array.isArray(items)) {
        console.error(`${LOG} ${label} response was not an array`, items)
        return []
      }
      console.info(`${LOG} loaded ${items.length} ${label} subscription(s)`)
      return label === "Team"
        ? (items as TeamSubscriptionView[]).map(normalizeTeam)
        : (items as UserSubscriptionView[]).map(normalizeUser)
    } catch (error) {
      console.error(`${LOG} failed to load ${label} subscriptions from ${path}`, error)
      throw error
    }
  }

  setStatus("Loading subscriptions…")
  const [teamResult, userResult] = await Promise.allSettled([
    loadType("Team", "/apim/team-subscriptions"),
    loadType("User", "/user-subscriptions"),
  ])

  const rows: SubscriptionRow[] = []
  if (teamResult.status === "fulfilled") rows.push(...teamResult.value)
  if (userResult.status === "fulfilled") rows.push(...userResult.value)

  if (!tableBody) return
  tableBody.innerHTML = ""

  const teamFailed = teamResult.status === "rejected"
  const userFailed = userResult.status === "rejected"

  if (teamFailed && userFailed) {
    console.error(`${LOG} both subscription sources failed`, {team: teamResult.reason, user: userResult.reason})
    setStatus("Failed to load subscriptions. See the browser console for details.", "error")
    return
  }

  for (const row of rows) {
    tableBody.appendChild(renderRow(row))
  }

  if (rows.length === 0) {
    if (emptyState) emptyState.hidden = false
  }

  if (teamFailed || userFailed) {
    const failed = teamFailed ? "team" : "user"
    setStatus(`Loaded subscriptions, but the ${failed} subscriptions could not be loaded. See the console for details.`, "error")
  } else {
    setStatus("")
  }
}

main()
