export type Values = {
  createHeading: string
  typeLabel: string
  typePersonalLabel: string
  typeGroupLabel: string
  nameLabel: string
  namePlaceholder: string
  groupFieldLabel: string
  groupPlaceholder: string
  groupDisclaimer: string
  personalButtonLabel: string
  groupButtonLabel: string
  functionBaseUrl: string
  scope: string
}

export const valuesDefault: Readonly<Values> = Object.freeze({
  createHeading: "Create a new subscription",
  typeLabel: "Type",
  typePersonalLabel: "👤 User",
  typeGroupLabel: "👥 Group",
  nameLabel: "Subscription name",
  namePlaceholder: "Your new product subscription name",
  groupFieldLabel: "APIM group",
  groupPlaceholder: "Select APIM Group",
  groupDisclaimer: "All members of this group will be able to manage this subscription.",
  personalButtonLabel: "Subscribe",
  groupButtonLabel: "Create",
  functionBaseUrl: "https://apimteam-func-3dexfwdm3jz34.azurewebsites.net/api",
  scope: "/apis",
})
