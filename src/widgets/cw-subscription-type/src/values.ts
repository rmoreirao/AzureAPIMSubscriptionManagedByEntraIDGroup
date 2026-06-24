export type Values = {
  title: string
  userSubscriptionLabel: string
  userSubscriptionDescription: string
  userSubscriptionUrl: string
  teamSubscriptionLabel: string
  teamSubscriptionDescription: string
  teamSubscriptionUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Choose a subscription type",
  userSubscriptionLabel: "User Subscription",
  userSubscriptionDescription: "A personal subscription tied to your own account.",
  userSubscriptionUrl: "/",
  teamSubscriptionLabel: "Team Subscription",
  teamSubscriptionDescription: "A shared subscription managed by an Entra ID group.",
  teamSubscriptionUrl: "/create-team-subscription",
})
