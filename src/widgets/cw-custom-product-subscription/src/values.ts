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
  teamSubscriptionDescription: "A shared subscription managed by an APIM group.",
  userPanelTitle: "Your subscriptions",
  teamPanelTitle: "Create Team Subscription",
  functionBaseUrl: "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api",
  scope: "/apis",
})
