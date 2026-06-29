# Specification: Enforce subscription access on every API + drop legacy "Entra ID" naming

| | |
|----------------|-----------------------------------------------------------|
| **Status**     | Approved — not yet implemented |
| **Date**       | 2026-06-29 |
| **Owner**      | @rmoreirao_microsoft |
| **Component**  | `src/GroupSubscriptions.Functions`, `src/widgets`, `infra` |

## 1. Summary

For every backend API action on an APIM subscription (showing the primary/secondary key,
regenerate, cancel, rename, list), the caller **must** be verified to have access to that
subscription **before** the action runs.

In addition, the codebase still uses **"Entra ID"** naming for what is actually an **APIM group**
flow. Group membership is resolved entirely from **APIM groups** via the APIM control plane
(`ApimGroupService`); there is **no** Entra ID / Microsoft Graph involvement. The legacy naming
(`entraIdGroup`, `EntraIdGroup`, `EntraGroup`) is misleading and will be renamed.

## 2. Motivation

- **Security hardening / defense-in-depth.** Make the per-action access checks consistent and
  ensure the group authorization cannot be influenced by client-supplied input.
- **Naming correctness.** Remove "Entra ID" references that no longer reflect the implementation.

## 3. Current behavior

| Endpoint | Action(s) | Current access check |
|----------|-----------|----------------------|
| `GET /api/user-subscriptions` | list | Filtered by owner server-side ✅ |
| `POST /api/user-subscriptions` | create | Owner forced to caller ✅ |
| `GET\|POST /api/user-subscriptions/{subscriptionId}/{action}` | keys / regenerate / cancel / rename | `IsOwnedByUserAsync` before any action ✅ |
| `GET /api/apim/users/{userId}/groups` | list groups | `userId == caller` ✅ |
| `GET /api/apim/group-subscriptions` | list | Groups resolved server-side ✅ |
| `POST /api/apim/group-subscriptions` | create | `IsMemberOfGroupAsync` ✅ |
| `GET\|POST /api/apim/group-subscriptions/{entraIdGroup}/{subscriptionId}/{action}` | keys / regenerate / cancel / rename | **Trusts the URL group**, then checks membership + record-belongs-to-group ⚠️ |

### Identified weakness

`ManageGroupSubscriptionApim` takes the owning group from the URL
(`/apim/group-subscriptions/{entraIdGroup}/{subscriptionId}/{action}`). Although it then verifies
membership and that the record belongs to that group (so it is not exploitable today), the owning
group should be **derived from the stored subscription record**, never from client input.

## 4. Requirements

### Functional

- **FR-1** Every group-subscription manage action must resolve the owning APIM group from the
  persisted subscription record (looked up by `subscriptionId`), then verify the caller is a member
  of that group, **before** performing the action.
- **FR-2** If no record exists for the `subscriptionId`, respond **404 Not Found**.
- **FR-3** If the caller is not a member of the owning group, respond **403 Forbidden**.
- **FR-4** User-subscription endpoints keep their existing ownership checks (no behavior change).
- **FR-5** The group id is removed from the manage route; clients address the action by
  `subscriptionId` only.

### Non-functional

- **NFR-1** No Entra ID / Microsoft Graph dependency is introduced or implied by naming.
- **NFR-2** Wire/JSON and storage field names reflect APIM-group semantics.
- **NFR-3** Changes must compile: Functions (`dotnet build`), widgets (tsc/build), Bicep
  (`az bicep build`). No automated test suite exists in the repo (out of scope).

## 5. Design

### 5.1 Authorization model (group subscriptions)

```
authenticate(caller)
record = repo.GetBySubscriptionIdAsync(subscriptionId)   # cross-partition lookup
if record == null            -> 404
if !IsMemberOfGroupAsync(caller, record.ApimGroup) -> 403
perform(action)              # primary-key / secondary-key / regenerate* / cancel / rename
```

The URL no longer carries the group:

```
/api/apim/group-subscriptions/{subscriptionId}/{action}
```

### 5.2 Renaming (canonical names)

| Old | New |
|-----|-----|
| JSON field `entraIdGroup` | `apimGroup` |
| `GroupSubscription.EntraIdGroup` | `GroupSubscription.ApimGroup` |
| `CreateGroupSubscriptionRequest.EntraIdGroup` | `…ApimGroup` |
| `GroupSubscriptionView.EntraIdGroup` | `…ApimGroup` |
| C# DTO class `EntraGroup` | `ApimGroup` |
| TS type `EntraGroup` | `ApimGroup` |
| TS field `entraIdGroup` | `apimGroup` |
| Cosmos partition key path `/entraIdGroup` | `/apimGroup` |
| Route `/group-subscriptions/{entraIdGroup}/{subscriptionId}/{action}` | `/group-subscriptions/{subscriptionId}/{action}` |

> `GroupSubscription.GroupId` / `GroupName` already duplicate the group id/name and are left
> unchanged to limit blast radius (flagged as optional future cleanup).

### 5.3 Cosmos partition key

The container partition key path is **immutable**. Changing `/entraIdGroup` → `/apimGroup`
requires recreating the container and migrating existing documents (mapping `entraIdGroup` →
`apimGroup`).

## 6. Implementation plan

### Phase 1 — Authorization hardening (core)
1. Add `CosmosRepository.GetBySubscriptionIdAsync(subscriptionId)` (cross-partition query on
   `c.subscriptionId`).
2. Rework `ManageGroupSubscriptionApim`: drop `{entraIdGroup}` from the route; load record by
   `subscriptionId` (404 if missing); derive owning group; verify membership (403); then act.
3. Update `infra/modules/apim.bicep` (`manage-group-subscription` urlTemplate + remove
   `entraIdGroup` template parameter).
4. Update `cw-group-subscriptions` `actionBasePath` to drop the group segment.

### Phase 2 — Rename legacy Entra* → APIM-group
5. `Models/GroupSubscription.cs`: rename fields, `[JsonPropertyName("apimGroup")]`, rename request +
   view fields/mapping, rename `EntraGroup` DTO → `ApimGroup`.
6. `CosmosRepository`: rename parameters, partition-key references (`subscription.ApimGroup`), and
   query text (`c.apimGroup`) across `UpsertAsync`, `GetAsync`, `GetByGroupsAsync`,
   `CountByGroupAndProductAsync`, `DeleteAsync`.
7. `SaveGroupSubscriptionApim`, `GetGroupSubscriptionsApim`, `ApimGroupService` usages/comments.
8. Widgets (`cw-group-subscriptions`, `cw-custom-product-subscription`): rename TS type, fields,
   request body key (`apimGroup`), display fallbacks.
9. `README.md` + code/XML comments referencing Entra ID for the group flow.

### Phase 3 — Cosmos partition key + data migration
10. `infra/modules/cosmos.bicep`: partition key path `/entraIdGroup` → `/apimGroup` (container
    recreate).
11. Regenerate `infra/main.json` via `az bicep build`.
12. One-off migration script (copy docs into recreated container, mapping `entraIdGroup` →
    `apimGroup`) or documented container recreate for the dev environment.

### Phase 4 — Validation & docs
13. `dotnet build` the Functions project.
14. Type-check/build both widgets.
15. `az bicep build` for `apim.bicep` + `cosmos.bicep`.
16. Document the final authorization model in `README.md`.

## 7. Acceptance criteria

- A caller who is **not** a member of a group subscription's owning APIM group receives **403** on
  every manage action, including key reveal/regenerate, cancel, and rename.
- An unknown `subscriptionId` returns **404**.
- The owning group is taken from the stored record, never from the request.
- No source identifier, JSON field, route, or doc references "Entra" for the group flow.
- Functions, both widgets, and Bicep all build successfully.

## 8. Risks & rollout

- **Breaking changes:** route shape, JSON wire field, and Cosmos partition key all change. Deploy in
  order — **infra** (container recreate + APIM operation) → **Functions** → **widgets** — with the
  data migration applied before Functions start serving the renamed contract.
- The `manage-group-subscription` APIM operation HTTP method is left as currently defined to avoid
  scope creep.

## 9. Out of scope

- Automated tests (no test project exists in the repo).
- Consolidating the redundant `GroupId`/`GroupName` fields.
- Any change to the user-subscription authorization (already correct).
