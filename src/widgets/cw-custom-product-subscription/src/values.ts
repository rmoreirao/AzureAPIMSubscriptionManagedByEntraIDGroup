export type Values = {
  title: string
  userSubscriptionLabel: string
  userSubscriptionDescription: string
  groupSubscriptionLabel: string
  groupSubscriptionDescription: string
  userPanelTitle: string
  groupPanelTitle: string
  functionBaseUrl: string
  scope: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Choose a subscription type",
  userSubscriptionLabel: "User Subscription",
  userSubscriptionDescription: "A personal subscription tied to your own account.",
  groupSubscriptionLabel: "Group Subscription",
  groupSubscriptionDescription: "A shared subscription managed by an APIM group.",
  userPanelTitle: "Your subscriptions",
  groupPanelTitle: "Create Group Subscription",
  functionBaseUrl: "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api",
  scope: "/apis",
})
