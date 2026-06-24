export type Values = {
  title: string
  userSubscriptionLabel: string
  userSubscriptionDescription: string
  teamSubscriptionLabel: string
  teamSubscriptionDescription: string
  userPanelTitle: string
  teamPanelTitle: string
  functionBaseUrl: string
  scope: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Choose a subscription type",
  userSubscriptionLabel: "User Subscription",
  userSubscriptionDescription: "A personal subscription tied to your own account.",
  teamSubscriptionLabel: "Team Subscription",
  teamSubscriptionDescription: "A shared subscription managed by an Entra ID group.",
  userPanelTitle: "Your subscriptions",
  teamPanelTitle: "Create Team Subscription",
  functionBaseUrl: "https://<your-function-app>.azurewebsites.net/api",
  scope: "/apis",
})
