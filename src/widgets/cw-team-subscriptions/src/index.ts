import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

type TeamSubscriptionView = {
  id: string
  subscriptionId: string
  subscriptionName: string
  entraIdGroup: string
  teamName: string
  dateCreated: string
  primaryKey?: string | null
  secondaryKey?: string | null
}

const MASK = "••••••••••••••••"

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
}

function formatDate(value: string): string {
  const date = new Date(value)
  return isNaN(date.getTime()) ? value : date.toLocaleDateString()
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

function keyCell(initialKey: string | null | undefined): HTMLTableCellElement {
  const key = initialKey ?? ""
  let revealed = false
  const value = el("span", {className: "key-value", textContent: key ? MASK : "—"})
  const toggle = el("button", {
    type: "button",
    className: "link-btn",
    textContent: key ? "Show" : "",
  })
  if (key) {
    toggle.addEventListener("click", () => {
      revealed = !revealed
      value.textContent = revealed ? key : MASK
      toggle.textContent = revealed ? "Hide" : "Show"
    })
  }
  return el("td", {}, [value, toggle])
}

async function main(): Promise<void> {
  const values = getValues(valuesDefault)
  const secrets = await askForSecrets("app")
  const apiFetch = createApiClient(secrets, values.functionBaseUrl)

  setText("title", values.title)

  const status = document.getElementById("status")
  const tableBody = document.getElementById("tableBody") as HTMLTableSectionElement | null
  const emptyState = document.getElementById("emptyState")

  function setStatus(message: string, kind: "info" | "error" | "success" = "info"): void {
    if (!status) return
    status.textContent = message
    status.className = `status status--${kind}`
  }

  function renderRow(item: TeamSubscriptionView): HTMLTableRowElement {
    const regenerateBtn = el("button", {type: "button", className: "btn-secondary", textContent: "Regenerate Keys"})
    const cancelBtn = el("button", {type: "button", className: "btn-danger", textContent: "Cancel"})

    const primaryCell = keyCell(item.primaryKey)
    const secondaryCell = keyCell(item.secondaryKey)

    const row = el("tr", {}, [
      el("td", {textContent: item.subscriptionName}),
      el("td", {textContent: item.teamName || item.entraIdGroup}),
      primaryCell,
      secondaryCell,
      el("td", {textContent: formatDate(item.dateCreated)}),
      el("td", {className: "actions"}, [regenerateBtn, cancelBtn]),
    ])

    const basePath = `/team-subscriptions/${encodeURIComponent(item.entraIdGroup)}/${encodeURIComponent(item.subscriptionId)}`

    regenerateBtn.addEventListener("click", async () => {
      regenerateBtn.disabled = true
      setStatus(`Regenerating keys for "${item.subscriptionName}"…`)
      try {
        const response = await apiFetch(`${basePath}/regenerate`, {method: "POST"})
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const keys: {primaryKey?: string; secondaryKey?: string} = await response.json()
        row.replaceChild(keyCell(keys.primaryKey), primaryCell)
        row.replaceChild(keyCell(keys.secondaryKey), secondaryCell)
        setStatus(`Keys regenerated for "${item.subscriptionName}".`, "success")
      } catch {
        setStatus(`Failed to regenerate keys for "${item.subscriptionName}".`, "error")
      } finally {
        regenerateBtn.disabled = false
      }
    })

    cancelBtn.addEventListener("click", async () => {
      if (!window.confirm(`Cancel subscription "${item.subscriptionName}"? This cannot be undone.`)) return
      cancelBtn.disabled = true
      setStatus(`Cancelling "${item.subscriptionName}"…`)
      try {
        const response = await apiFetch(`${basePath}/cancel`, {method: "POST"})
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        row.remove()
        setStatus(`Subscription "${item.subscriptionName}" cancelled.`, "success")
        if (tableBody && tableBody.childElementCount === 0 && emptyState) {
          emptyState.hidden = false
        }
      } catch {
        setStatus(`Failed to cancel "${item.subscriptionName}".`, "error")
        cancelBtn.disabled = false
      }
    })

    return row
  }

  setStatus("Loading team subscriptions…")
  try {
    const response = await apiFetch("/team-subscriptions")
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const items: TeamSubscriptionView[] = await response.json()

    if (!tableBody) return
    tableBody.innerHTML = ""
    if (items.length === 0) {
      if (emptyState) emptyState.hidden = false
      setStatus("")
      return
    }
    for (const item of items) {
      tableBody.appendChild(renderRow(item))
    }
    setStatus("")
  } catch {
    setStatus("Failed to load team subscriptions.", "error")
  }
}

main()
