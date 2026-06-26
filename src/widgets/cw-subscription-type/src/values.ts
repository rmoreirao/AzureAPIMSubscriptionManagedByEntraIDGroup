export type Values = {
  title: string
  userSubscriptionLabel: string
  userSubscriptionDescription: string
  userSubscriptionUrl: string
  groupSubscriptionLabel: string
  groupSubscriptionDescription: string
  groupSubscriptionUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Choose a subscription type",
  userSubscriptionLabel: "User Subscription",
  userSubscriptionDescription: "A personal subscription tied to your own account.",
  userSubscriptionUrl: "/",
  groupSubscriptionLabel: "Group Subscription",
  groupSubscriptionDescription: "A shared subscription managed by an Entra ID group.",
  groupSubscriptionUrl: "/create-group-subscription",
})
