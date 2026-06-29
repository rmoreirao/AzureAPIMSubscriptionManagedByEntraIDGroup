import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

const LOG = "[cw-group-subscriptions]"

type SubscriptionType = "User" | "Group"

/** Raw shape returned by GET /apim/group-subscriptions. */
type GroupSubscriptionView = {
  subscriptionId: string
  subscriptionName: string
  entraIdGroup: string
  groupName: string
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

function normalizeGroup(item: GroupSubscriptionView): SubscriptionRow {
  return {
    type: "Group",
    subscriptionId: item.subscriptionId,
    subscriptionName: item.subscriptionName,
    group: item.groupName || item.entraIdGroup || "",
    entraIdGroup: item.entraIdGroup || "",
    product: item.product || "",
    state: item.state || "",
    dateCreated: item.dateCreated || "",
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
    return row.type === "Group"
      ? `/apim/group-subscriptions/${encodeURIComponent(row.entraIdGroup)}/${encodeURIComponent(row.subscriptionId)}`
      : `/user-subscriptions/${encodeURIComponent(row.subscriptionId)}`
  }

  function renderRow(row: SubscriptionRow): HTMLTableRowElement {
    const active = isActive(row.state)

    // Each key cell fetches its key on demand (never sent with the list), reveals it with a
    // Copy control, then re-masks once the value has been copied to avoid lingering exposure.
    function makeKeyCell(keyAction: "primary-key" | "secondary-key"): {cell: HTMLTableCellElement; reveal: (value: string) => void; mask: () => void} {
      const value = el("span", {className: "key-value", textContent: MASK})
      const toggle = el("button", {type: "button", className: "link-btn", textContent: "Show"})
      const cell = el("td", {}, [value, toggle])

      function mask(): void {
        value.textContent = MASK
        toggle.textContent = "Show"
      }

      function reveal(key: string): void {
        value.textContent = key
        toggle.textContent = "Copy"
      }

      toggle.addEventListener("click", async (e) => {
        e.stopPropagation()
        if (toggle.textContent === "Copy") {
          try {
            await navigator.clipboard.writeText(value.textContent || "")
            setStatus("Key copied to clipboard.", "success")
          } catch {
            setStatus("Could not copy the key to the clipboard.", "error")
          }
          mask()
          return
        }
        toggle.disabled = true
        try {
          const response = await apiFetch(`${actionBasePath(row)}/${keyAction}`)
          if (!response.ok) throw new Error(`HTTP ${response.status}`)
          const keys: {primaryKey?: string; secondaryKey?: string} = await response.json()
          const key = keyAction === "primary-key" ? keys.primaryKey : keys.secondaryKey
          if (!key) throw new Error("empty key")
          reveal(key)
        } catch (error) {
          console.error(`${LOG} failed to fetch ${keyAction} for "${row.subscriptionName}" (${row.subscriptionId})`, error)
          setStatus(`Failed to load the key for "${row.subscriptionName}".`, "error")
        } finally {
          toggle.disabled = false
        }
      })

      return {cell, reveal, mask}
    }

    const primary = makeKeyCell("primary-key")
    const secondary = makeKeyCell("secondary-key")

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
      tr.append(primary.cell, secondary.cell)
    } else {
      // Inactive subscriptions have no usable keys — show a single explanatory message.
      tr.append(el("td", {className: "inactive-note", colSpan: 2}, [el("em", {textContent: "The subscription is not active"})]))
    }

    tr.append(el("td", {textContent: formatDate(row.dateCreated)}))

    // Actions: a kebab menu, only offered for active subscriptions.
    const actionsCell = el("td", {className: "actions-col"})
    if (active) {
      const menu = el("div", {className: "menu", hidden: true})

      // Regenerates a single key and reveals it (with copy/auto-hide via the matching cell).
      function makeRegenItem(label: string, action: "regenerate-primary" | "regenerate-secondary", which: typeof primary | typeof secondary): HTMLButtonElement {
        const item = el("button", {type: "button", className: "menu-item", textContent: label})
        item.addEventListener("click", async (e) => {
          e.stopPropagation()
          closeMenu()
          item.disabled = true
          setStatus(`Regenerating ${label.toLowerCase()} for "${row.subscriptionName}"…`)
          try {
            const response = await apiFetch(`${actionBasePath(row)}/${action}`, {method: "POST"})
            if (!response.ok) throw new Error(`HTTP ${response.status}`)
            const keys: {primaryKey?: string; secondaryKey?: string} = await response.json()
            const key = action === "regenerate-primary" ? keys.primaryKey : keys.secondaryKey
            if (key) which.reveal(key)
            setStatus(`${label} regenerated for "${row.subscriptionName}". Copy it now — it will be hidden after copying.`, "success")
          } catch (error) {
            console.error(`${LOG} failed to ${action} for "${row.subscriptionName}" (${row.subscriptionId})`, error)
            setStatus(`Failed to regenerate the key for "${row.subscriptionName}".`, "error")
          } finally {
            item.disabled = false
          }
        })
        return item
      }

      const regenPrimaryItem = makeRegenItem("Regenerate primary key", "regenerate-primary", primary)
      const regenSecondaryItem = makeRegenItem("Regenerate secondary key", "regenerate-secondary", secondary)

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
          // Keep the row visible but mark it cancelled (no usable keys), matching what the
          // grid shows after a reload, rather than removing it from the table.
          row.state = "Cancelled"
          tr.replaceWith(renderRow(row))
          setStatus(`Subscription "${row.subscriptionName}" cancelled.`, "success")
        } catch (error) {
          console.error(`${LOG} failed to cancel "${row.subscriptionName}" (${row.subscriptionId})`, error)
          setStatus(`Failed to cancel "${row.subscriptionName}".`, "error")
          cancelItem.disabled = false
        }
      })

      menu.append(regenPrimaryItem, regenSecondaryItem, cancelItem)

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
      return label === "Group"
        ? (items as GroupSubscriptionView[]).map(normalizeGroup)
        : (items as UserSubscriptionView[]).map(normalizeUser)
    } catch (error) {
      console.error(`${LOG} failed to load ${label} subscriptions from ${path}`, error)
      throw error
    }
  }

  setStatus("Loading subscriptions…")
  const [groupResult, userResult] = await Promise.allSettled([
    loadType("Group", "/apim/group-subscriptions"),
    loadType("User", "/user-subscriptions"),
  ])

  const rows: SubscriptionRow[] = []
  if (groupResult.status === "fulfilled") rows.push(...groupResult.value)
  if (userResult.status === "fulfilled") rows.push(...userResult.value)

  if (!tableBody) return
  tableBody.innerHTML = ""

  const groupFailed = groupResult.status === "rejected"
  const userFailed = userResult.status === "rejected"

  if (groupFailed && userFailed) {
    console.error(`${LOG} both subscription sources failed`, {group: groupResult.reason, user: userResult.reason})
    setStatus("Failed to load subscriptions. See the browser console for details.", "error")
    return
  }

  for (const row of rows) {
    tableBody.appendChild(renderRow(row))
  }

  if (rows.length === 0) {
    if (emptyState) emptyState.hidden = false
  }

  if (groupFailed || userFailed) {
    const failed = groupFailed ? "group" : "user"
    setStatus(`Loaded subscriptions, but the ${failed} subscriptions could not be loaded. See the console for details.`, "error")
  } else {
    setStatus("")
  }
}

main()
