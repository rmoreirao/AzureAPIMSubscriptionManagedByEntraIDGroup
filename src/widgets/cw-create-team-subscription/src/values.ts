export type Values = {
  title: string
  functionBaseUrl: string
  scope: string
  successRedirectUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Create Team Subscription",
  functionBaseUrl: "https://<your-function-app>.azurewebsites.net/api",
  scope: "/apis",
  successRedirectUrl: "/team-subscriptions",
})
