export type Values = {
  title: string
  functionBaseUrl: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  title: "Group Subscriptions",
  functionBaseUrl: "https://<your-func-app>.azurewebsites.net/api",
})
