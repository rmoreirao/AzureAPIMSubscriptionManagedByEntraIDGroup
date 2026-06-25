import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient, ApiFetch} from "./api"

type EntraGroup = {id: string; displayName: string}

type StatusKind = "info" | "error" | "success"

type SubscriptionType = "User" | "Team"

/** Raw shape returned by GET /apim/team-subscriptions. */
type TeamSubscriptionView = {
  subscriptionName: string
  entraIdGroup?: string | null
  teamName?: string | null
}

/** Raw shape returned by GET /user-subscriptions. */
type UserSubscriptionView = {
  subscriptionName: string
}

/** Normalized row used to render the Active subscriptions table. */
type SubscriptionRow = {
  type: SubscriptionType
  group: string
  name: string
}

const LOG = "[cw-create-team-subscription]"

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
}

async function loadSubscriptions(
  apiFetch: ApiFetch,
  label: SubscriptionType,
  path: string
): Promise<SubscriptionRow[]> {
  const response = await apiFetch(path)
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  const items = await response.json()
  if (!Array.isArray(items)) {
    console.error(`${LOG} ${label} response was not an array`, items)
    return []
  }
  if (label === "Team") {
    return (items as TeamSubscriptionView[]).map(item => ({
      type: "Team",
      group: item.teamName || item.entraIdGroup || "—",
      name: item.subscriptionName,
    }))
  }
  return (items as UserSubscriptionView[]).map(item => ({
    type: "User",
    group: "—",
    name: item.subscriptionName,
  }))
}

async function main(): Promise<void> {
  const values = getValues(valuesDefault)
  const secrets = await askForSecrets("app")
  const apiFetch = createApiClient(secrets, values.functionBaseUrl)

  setText("title", values.title)

  const form = document.getElementById("form") as HTMLFormElement | null
  const nameInput = document.getElementById("subscriptionName") as HTMLInputElement | null
  const groupSelect = document.getElementById("entraIdGroup") as HTMLSelectElement | null
  const submitButton = document.getElementById("submit") as HTMLButtonElement | null
  const status = document.getElementById("status")

  const subsBody = document.getElementById("subsBody") as HTMLTableSectionElement | null
  const subsCount = document.getElementById("subsCount")
  const subsEmpty = document.getElementById("subsEmpty")
  const subsStatus = document.getElementById("subsStatus")

  function setStatus(message: string, kind: StatusKind = "info"): void {
    if (!status) return
    status.textContent = message
    status.className = `status status--${kind}`
  }

  function setSubsStatus(message: string, kind: StatusKind = "info"): void {
    if (!subsStatus) return
    subsStatus.textContent = message
    subsStatus.className = `status status--${kind}`
  }

  function renderSubscriptions(rows: SubscriptionRow[]): void {
    if (!subsBody) return
    subsBody.innerHTML = ""
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
      subsBody.appendChild(tr)
    }
    if (subsCount) subsCount.textContent = rows.length ? `${rows.length} total` : ""
    if (subsEmpty) subsEmpty.hidden = rows.length > 0
  }

  async function refreshSubscriptions(): Promise<void> {
    setSubsStatus("Loading your subscriptions…")
    const [teamResult, userResult] = await Promise.allSettled([
      loadSubscriptions(apiFetch, "Team", "/apim/team-subscriptions"),
      loadSubscriptions(apiFetch, "User", "/user-subscriptions"),
    ])

    const rows: SubscriptionRow[] = []
    if (teamResult.status === "fulfilled") rows.push(...teamResult.value)
    if (userResult.status === "fulfilled") rows.push(...userResult.value)

    const teamFailed = teamResult.status === "rejected"
    const userFailed = userResult.status === "rejected"

    if (teamFailed && userFailed) {
      console.error(`${LOG} both subscription sources failed`, {team: teamResult, user: userResult})
      setSubsStatus("Failed to load your subscriptions. See the browser console for details.", "error")
      renderSubscriptions([])
      if (subsEmpty) subsEmpty.hidden = true
      return
    }

    renderSubscriptions(rows)

    if (teamFailed || userFailed) {
      const failed = teamFailed ? "team" : "user"
      setSubsStatus(`Loaded subscriptions, but the ${failed} subscriptions could not be loaded.`, "error")
    } else {
      setSubsStatus("")
    }
  }

  if (!secrets.userId) {
    setStatus("Unable to determine the current user.", "error")
    return
  }

  // Show the caller's existing subscriptions above the create form.
  void refreshSubscriptions()

  // Populate the APIM Group dropdown with the caller's current groups.
  try {
    const response = await apiFetch(`/apim/users/${encodeURIComponent(secrets.userId)}/groups`)
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const groups: EntraGroup[] = await response.json()
    if (groupSelect) {
      groupSelect.innerHTML = ""
      if (groups.length === 0) {
        setStatus("You are not a member of any APIM group.", "error")
      }
      for (const group of groups) {
        const option = document.createElement("option")
        option.value = group.id
        option.textContent = group.displayName || group.id
        groupSelect.appendChild(option)
      }
    }
  } catch {
    setStatus("Failed to load your APIM groups.", "error")
    return
  }

  form?.addEventListener("submit", async event => {
    event.preventDefault()
    const subscriptionName = nameInput?.value.trim()
    const entraIdGroup = groupSelect?.value
    if (!subscriptionName || !entraIdGroup) {
      setStatus("Please provide a subscription name and select a group.", "error")
      return
    }

    if (submitButton) submitButton.disabled = true
    setStatus("Creating subscription…")
    try {
      const teamName = groupSelect?.selectedOptions[0]?.textContent ?? ""
      const response = await apiFetch("/apim/team-subscriptions", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({subscriptionName, entraIdGroup, teamName, scope: values.scope}),
      })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      setStatus("Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
      await refreshSubscriptions()
      if (values.successRedirectUrl) {
        setTimeout(() => window.open(values.successRedirectUrl, "_top"), 1200)
      }
    } catch {
      setStatus("Failed to create the subscription.", "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

main()
