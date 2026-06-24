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

  setupUserPanel(apiFetch, values.scope)
  setupTeamPanel(apiFetch, values.scope)
}

// ---------------------------------------------------------------------------
// User subscription panel: list existing subscriptions + create form.
// ---------------------------------------------------------------------------
async function loadUserSubscriptions(apiFetch: ApiFetch): Promise<void> {
  const list = document.getElementById("userSubList")
  if (!list) return
  list.innerHTML = `<p class="user-sub-empty">Loading your subscriptions…</p>`
  try {
    const response = await apiFetch("/user-subscriptions")
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const subscriptions: UserSubscription[] = await response.json()
    renderUserSubscriptions(list, subscriptions)
  } catch {
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
      const response = await apiFetch("/user-subscriptions", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({subscriptionName, scope}),
      })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      setStatus("userStatus", "Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
      await loadUserSubscriptions(apiFetch)
    } catch {
      setStatus("userStatus", "Failed to create the subscription.", "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

// ---------------------------------------------------------------------------
// Team subscription panel: pick an Entra ID group + create form.
// ---------------------------------------------------------------------------
async function loadGroups(apiFetch: ApiFetch, userId: string): Promise<void> {
  const groupSelect = document.getElementById("entraIdGroup") as HTMLSelectElement | null

  if (!userId) {
    setStatus("teamStatus", "Unable to determine the current user.", "error")
    return
  }

  try {
    const response = await apiFetch(`/users/${encodeURIComponent(userId)}/groups`)
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const groups: EntraGroup[] = await response.json()
    if (groupSelect) {
      groupSelect.innerHTML = ""
      if (groups.length === 0) {
        setStatus("teamStatus", "You are not a member of any Entra ID group.", "error")
      }
      for (const group of groups) {
        const option = document.createElement("option")
        option.value = group.id
        option.textContent = group.displayName || group.id
        groupSelect.appendChild(option)
      }
    }
  } catch {
    setStatus("teamStatus", "Failed to load your Entra ID groups.", "error")
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
      const response = await apiFetch("/team-subscriptions", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({subscriptionName, entraIdGroup, teamName, scope}),
      })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      setStatus("teamStatus", "Subscription created successfully.", "success")
      if (nameInput) nameInput.value = ""
    } catch {
      setStatus("teamStatus", "Failed to create the subscription.", "error")
    } finally {
      if (submitButton) submitButton.disabled = false
    }
  })
}

main()
