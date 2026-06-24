export type Values = {
  title: string
  functionBaseUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Team Subscriptions",
  functionBaseUrl: "https://<your-function-app>.azurewebsites.net/api",
})
