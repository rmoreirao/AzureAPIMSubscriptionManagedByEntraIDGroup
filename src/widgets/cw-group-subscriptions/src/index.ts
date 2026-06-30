import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

const LOG = "[cw-group-subscriptions]"

type SubscriptionType = "User" | "Group"

/** Raw shape returned by GET /apim/group-subscriptions. */
type GroupSubscriptionView = {
  subscriptionId: string
  subscriptionName: string
  apimGroup: string
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
  apimGroup: string
  product: string
  state: string
  dateCreated: string
}

const MASK = "••••••••••••••••"

// Fluent-style inline icons matching the OOB Dev Portal rename controls.
const ICON_EDIT = `<svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path d="M14.6 2.6a1.5 1.5 0 0 1 2.1 0l.7.7a1.5 1.5 0 0 1 0 2.1l-8.4 8.4-3 .8.8-3 8.4-8.4ZM4 14v2h2l8.3-8.3-2-2L4 14Z"/></svg>`
const ICON_SAVE = `<svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path d="M7.5 13.6 4 10.1l-1 1L7.5 16 17 6.5l-1-1z"/></svg>`
const ICON_CANCEL = `<svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path d="M4.1 4.2 4.9 4 10 9.3 15.1 4l.8.2.2.8L10.7 10l5.4 5.1-.2.8-.8.2L10 10.7 4.9 16l-.8-.2-.2-.8L9.3 10 3.9 5z"/></svg>`

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
    group: item.groupName || item.apimGroup || "",
    apimGroup: item.apimGroup || "",
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
    apimGroup: "",
    product: item.product || item.scope || "",
    state: item.state || "",
    dateCreated: item.dateCreated || "",
  }
}

async function main(): Promise<void> {
  const values = getValues(valuesDefault)
  const secrets = await askForSecrets("app")
  const apiFetch = createApiClient(secrets, values.functionBaseUrl)

  const status = document.getElementById("status")
  const tableBody = document.getElementById("tableBody") as HTMLTableSectionElement | null
  const emptyState = document.getElementById("emptyState")

  const filterSearch = document.getElementById("filterSearch") as HTMLInputElement | null
  const filterType = document.getElementById("filterType") as HTMLSelectElement | null
  const filterProduct = document.getElementById("filterProduct") as HTMLSelectElement | null
  const filterState = document.getElementById("filterState") as HTMLSelectElement | null
  const filterClear = document.getElementById("filterClear")
  const filterCount = document.getElementById("filterCount")

  // All loaded rows (User + Group). The visible table is always a filtered view of this.
  let allRows: SubscriptionRow[] = []

  // Tracks the kebab menu that is currently open so we can close it on an outside click.
  // The menu is portaled into document.body while open (position: fixed) so it escapes the
  // table's `overflow: hidden` and the scroll container, then detached again on close.
  let openMenu: HTMLElement | null = null

  // Closes the menu while scrolling/resizing; bound only while a menu is open.
  function onViewportChange(): void {
    closeMenu()
  }

  function closeMenu(): void {
    if (openMenu) {
      openMenu.hidden = true
      openMenu.remove()
      openMenu = null
      window.removeEventListener("scroll", onViewportChange, true)
      window.removeEventListener("resize", onViewportChange)
    }
  }

  // Portals `menu` into the body and positions it next to `anchor` (the kebab button),
  // flipping upward when there isn't enough room below and clamping to the viewport.
  function openMenuAt(menu: HTMLElement, anchor: HTMLElement): void {
    const gap = 4
    const margin = 8
    document.body.append(menu)
    menu.hidden = false
    openMenu = menu

    const rect = anchor.getBoundingClientRect()
    const menuWidth = menu.offsetWidth
    const menuHeight = menu.offsetHeight

    let left = rect.right - menuWidth
    left = Math.min(Math.max(left, margin), window.innerWidth - menuWidth - margin)

    const spaceBelow = window.innerHeight - rect.bottom
    let top = rect.bottom + gap
    if (spaceBelow < menuHeight + gap + margin && rect.top > menuHeight + gap) {
      top = rect.top - menuHeight - gap
    }

    menu.style.left = `${Math.round(left)}px`
    menu.style.top = `${Math.round(top)}px`

    window.addEventListener("scroll", onViewportChange, true)
    window.addEventListener("resize", onViewportChange)
  }

  document.addEventListener("click", () => closeMenu())

  function setStatus(message: string, kind: "info" | "error" | "success" = "info"): void {
    if (!status) return
    status.textContent = message
    status.className = `status status--${kind}`
  }

  function actionBasePath(row: SubscriptionRow): string {
    return row.type === "Group"
      ? `/apim/group-subscriptions/${encodeURIComponent(row.subscriptionId)}`
      : `/user-subscriptions/${encodeURIComponent(row.subscriptionId)}`
  }

  function renderRow(row: SubscriptionRow): HTMLTableRowElement {
    const active = isActive(row.state)

    // Name cell with an in-place rename: a pencil icon appears on hover; clicking it swaps the name
    // for a textbox with Save/Cancel icons that PATCH the subscription's display name (mirrors OOB).
    function makeNameCell(): HTMLTableCellElement {
      const cell = el("td", {className: "name-cell"})

      function renderDisplay(): void {
        cell.innerHTML = ""
        const text = el("span", {className: "name-text", textContent: row.subscriptionName})
        const edit = el("button", {
          type: "button",
          className: "icon-btn edit-icon",
          title: "Edit",
          innerHTML: ICON_EDIT,
        })
        edit.setAttribute("aria-label", "Edit")
        edit.addEventListener("click", e => {
          e.stopPropagation()
          renderEdit()
        })
        cell.append(text, edit)
      }

      function renderEdit(): void {
        cell.innerHTML = ""
        const input = el("input", {
          className: "name-input",
          type: "text",
          value: row.subscriptionName,
        }) as HTMLInputElement
        const save = el("button", {type: "button", className: "icon-btn", title: "Save", innerHTML: ICON_SAVE})
        save.setAttribute("aria-label", "Save")
        const cancel = el("button", {type: "button", className: "icon-btn", title: "Cancel", innerHTML: ICON_CANCEL})
        cancel.setAttribute("aria-label", "Cancel")

        async function commit(): Promise<void> {
          const newName = input.value.trim()
          if (!newName || newName === row.subscriptionName) {
            renderDisplay()
            return
          }
          save.disabled = cancel.disabled = input.disabled = true
          setStatus(`Renaming "${row.subscriptionName}"…`)
          try {
            const response = await apiFetch(`${actionBasePath(row)}/rename`, {
              method: "POST",
              headers: {"Content-Type": "application/json"},
              body: JSON.stringify({name: newName}),
            })
            if (!response.ok) throw new Error(`HTTP ${response.status}`)
            row.subscriptionName = newName
            setStatus(`Subscription renamed to "${newName}".`, "success")
          } catch (error) {
            console.error(`${LOG} failed to rename "${row.subscriptionName}" (${row.subscriptionId})`, error)
            setStatus(`Failed to rename "${row.subscriptionName}".`, "error")
          }
          renderDisplay()
        }

        save.addEventListener("click", e => {
          e.stopPropagation()
          void commit()
        })
        cancel.addEventListener("click", e => {
          e.stopPropagation()
          renderDisplay()
        })
        input.addEventListener("click", e => e.stopPropagation())
        input.addEventListener("keydown", e => {
          if (e.key === "Enter") void commit()
          else if (e.key === "Escape") renderDisplay()
        })
        cell.append(input, save, cancel)
        input.focus()
        input.select()
      }

      renderDisplay()
      return cell
    }

    // Each key cell fetches its key on demand (never sent with the list), reveals it with a
    // Copy control, then re-masks once the value has been copied to avoid lingering exposure.
    function makeKeyCell(keyAction: "primary-key" | "secondary-key"): {
      cell: HTMLTableCellElement
      reveal: (value: string) => void
      mask: () => void
    } {
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

      toggle.addEventListener("click", async e => {
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
          console.error(
            `${LOG} failed to fetch ${keyAction} for "${row.subscriptionName}" (${row.subscriptionId})`,
            error
          )
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
      makeNameCell(),
      el("td", {textContent: row.type}),
      el("td", {textContent: row.group || "—"}),
      el("td", {textContent: row.product || "—"})
    )

    const stateCell = el("td", {
      textContent: row.state || "—",
      className: active ? "state state--active" : "state state--inactive",
    })
    tr.append(stateCell)

    if (active) {
      tr.append(primary.cell, secondary.cell)
    } else {
      // Inactive subscriptions have no usable keys — show a single explanatory message.
      tr.append(
        el("td", {className: "inactive-note", colSpan: 2}, [el("em", {textContent: "The subscription is not active"})])
      )
    }

    tr.append(el("td", {textContent: formatDate(row.dateCreated)}))

    // Actions: a kebab menu, only offered for active subscriptions.
    const actionsCell = el("td", {className: "actions-col"})
    if (active) {
      const menu = el("div", {className: "menu", hidden: true})

      // Regenerates a single key and reveals it (with copy/auto-hide via the matching cell).
      function makeRegenItem(
        label: string,
        action: "regenerate-primary" | "regenerate-secondary",
        which: typeof primary | typeof secondary
      ): HTMLButtonElement {
        const item = el("button", {type: "button", className: "menu-item", textContent: label})
        item.addEventListener("click", async e => {
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
            setStatus(
              `${label} regenerated for "${row.subscriptionName}". Copy it now — it will be hidden after copying.`,
              "success"
            )
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

      const cancelItem = el("button", {
        type: "button",
        className: "menu-item menu-item--danger",
        textContent: "Cancel subscription",
      })
      cancelItem.addEventListener("click", async e => {
        e.stopPropagation()
        closeMenu()
        if (!window.confirm(`Cancel subscription "${row.subscriptionName}"? This cannot be undone.`)) return
        cancelItem.disabled = true
        setStatus(`Cancelling "${row.subscriptionName}"…`)
        try {
          const response = await apiFetch(`${actionBasePath(row)}/cancel`, {method: "POST"})
          if (!response.ok) throw new Error(`HTTP ${response.status}`)
          // Mark the row cancelled, then re-apply filters so the now-inactive row drops out
          // of the table when the (default) Active filter is in effect.
          row.state = "Cancelled"
          setStatus(`Subscription "${row.subscriptionName}" cancelled.`, "success")
          applyFilters()
        } catch (error) {
          console.error(`${LOG} failed to cancel "${row.subscriptionName}" (${row.subscriptionId})`, error)
          setStatus(`Failed to cancel "${row.subscriptionName}".`, "error")
          cancelItem.disabled = false
        }
      })

      menu.append(regenPrimaryItem, regenSecondaryItem, cancelItem)

      const kebab = el("button", {type: "button", className: "kebab", textContent: "⋯"})
      kebab.setAttribute("aria-label", "Subscription actions")
      kebab.addEventListener("click", e => {
        e.stopPropagation()
        const willOpen = openMenu !== menu
        closeMenu()
        if (willOpen) {
          openMenuAt(menu, kebab)
        }
      })

      actionsCell.append(kebab)
    }
    tr.append(actionsCell)

    return tr
  }

  // Fills the Product dropdown with the distinct, sorted product names found in the loaded rows,
  // preserving the leading "All products" option.
  function populateProducts(): void {
    if (!filterProduct) return
    while (filterProduct.options.length > 1) filterProduct.remove(1)
    const products = Array.from(new Set(allRows.map(r => r.product).filter(Boolean))).sort()
    for (const product of products) {
      filterProduct.appendChild(el("option", {value: product, textContent: product}))
    }
  }

  // Renders the table as a filtered view of `allRows` driven by the toolbar controls, and keeps
  // the result count and empty/no-match message in sync.
  function applyFilters(): void {
    if (!tableBody) return
    closeMenu()
    const term = (filterSearch?.value ?? "").trim().toLowerCase()
    const type = filterType?.value ?? ""
    const product = filterProduct?.value ?? ""
    const state = filterState?.value ?? ""

    const filtered = allRows.filter(row => {
      if (term && !row.subscriptionName.toLowerCase().includes(term)) return false
      if (type && row.type !== type) return false
      if (product && row.product !== product) return false
      if (state) {
        const active = isActive(row.state)
        if (state === "Active" && !active) return false
        if (state === "Inactive" && active) return false
      }
      return true
    })

    tableBody.innerHTML = ""
    for (const row of filtered) {
      tableBody.appendChild(renderRow(row))
    }

    if (filterCount) filterCount.innerHTML = `Showing <b>${filtered.length}</b> of ${allRows.length}`

    if (emptyState) {
      if (allRows.length === 0) {
        emptyState.textContent = "You have no subscriptions yet."
        emptyState.hidden = false
      } else if (filtered.length === 0) {
        emptyState.textContent = "No subscriptions match your filters."
        emptyState.hidden = false
      } else {
        emptyState.hidden = true
      }
    }
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

  const groupFailed = groupResult.status === "rejected"
  const userFailed = userResult.status === "rejected"

  if (groupFailed && userFailed) {
    console.error(`${LOG} both subscription sources failed`, {group: groupResult.reason, user: userResult.reason})
    setStatus("Failed to load subscriptions. See the browser console for details.", "error")
    return
  }

  allRows = rows
  populateProducts()

  for (const control of [filterSearch, filterType, filterProduct, filterState]) {
    control?.addEventListener("input", applyFilters)
  }
  filterClear?.addEventListener("click", () => {
    if (filterSearch) filterSearch.value = ""
    if (filterType) filterType.value = ""
    if (filterProduct) filterProduct.value = ""
    // The State filter defaults to "Active", so clearing restores that rather than "All states".
    if (filterState) filterState.value = "Active"
    applyFilters()
  })

  applyFilters()

  if (groupFailed || userFailed) {
    const failed = groupFailed ? "group" : "user"
    setStatus(
      `Loaded subscriptions, but the ${failed} subscriptions could not be loaded. See the console for details.`,
      "error"
    )
  } else {
    setStatus("")
  }
}

main()
