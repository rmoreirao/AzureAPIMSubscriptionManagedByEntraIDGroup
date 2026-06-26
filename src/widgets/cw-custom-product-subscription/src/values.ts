export type Values = {
  createHeading: string
  typeLabel: string
  typePersonalLabel: string
  typeGroupLabel: string
  nameLabel: string
  namePlaceholder: string
  groupFieldLabel: string
  personalButtonLabel: string
  groupButtonLabel: string
  functionBaseUrl: string
  scope: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  createHeading: "Create a new subscription",
  typeLabel: "Type",
  typePersonalLabel: "👤 Personal",
  typeGroupLabel: "👥 Group",
  nameLabel: "Subscription name",
  namePlaceholder: "Your new product subscription name",
  groupFieldLabel: "APIM group",
  personalButtonLabel: "Subscribe",
  groupButtonLabel: "Create",
  functionBaseUrl: "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api",
  scope: "/apis",
})
