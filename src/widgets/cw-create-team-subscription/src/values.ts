export type Values = {
  title: string
  functionBaseUrl: string
  scope: string
  successRedirectUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Create Team Subscription",
  functionBaseUrl: "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api",
  scope: "/apis",
  successRedirectUrl: "/team-subscriptions",
})
