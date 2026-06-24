import {getValues} from "@azure/api-management-custom-widgets-tools"
import {valuesDefault, Values} from "./values"

const values = getValues(valuesDefault)

function setText(id: string, text: string): void {
  const el = document.getElementById(id)
  if (el) el.innerText = text
}

function setupOption(cardId: string, labelId: string, descId: string, label: string, description: string, url: string): void {
  setText(labelId, label)
  setText(descId, description)
  const card = document.getElementById(cardId)
  if (!card) return
  card.addEventListener("click", () => {
    if (url) window.open(url, "_top")
  })
}

setText("title", values.title)

setupOption(
  "userSubscription",
  "userSubscriptionLabel",
  "userSubscriptionDescription",
  values.userSubscriptionLabel,
  values.userSubscriptionDescription,
  values.userSubscriptionUrl
)

setupOption(
  "teamSubscription",
  "teamSubscriptionLabel",
  "teamSubscriptionDescription",
  values.teamSubscriptionLabel,
  values.teamSubscriptionDescription,
  values.teamSubscriptionUrl
)

export type {Values}
