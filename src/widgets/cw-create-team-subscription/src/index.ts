import {askForSecrets, getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault} from "./values"
import {createApiClient} from "./api"

type EntraGroup = {id: string; displayName: string}

type StatusKind = "info" | "error" | "success"

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
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

  function setStatus(message: string, kind: StatusKind = "info"): void {
    if (!status) return
    status.textContent = message
    status.className = `status status--${kind}`
  }

  if (!secrets.userId) {
    setStatus("Unable to determine the current user.", "error")
    return
  }

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
