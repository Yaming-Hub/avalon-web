# Avalon Web — Azure App Service Deployment Guide

This document captures the full setup process for deploying the Avalon Web application to Azure App Service using GitHub Actions with OIDC authentication.

## Overview

| Item | Value |
|---|---|
| **Azure Resource Group** | `avolon-web` |
| **Azure Subscription ID** | `f3eecfde-cfdc-4b28-b997-c3cd03854318` |
| **Azure Tenant ID** | `604c4563-431d-4538-8339-e22291a6afb0` |
| **App Registration (appId)** | `8fc566f7-a49c-4714-8663-7c17309f5c10` |
| **App Registration (objectId)** | `7a48bbfb-7736-453b-b03d-a57de38a9c08` |
| **App Service Name** | `avolon-web` |
| **Public URL** | `https://avolon-web.azurewebsites.net` |
| **GitHub Repo** | `Yaming-Hub/avalon-web` |
| **Runtime** | .NET 8 on Linux |
| **Auth Method** | OIDC (Federated Identity Credentials) |

## Architecture

```
GitHub (push to main)
  └─► GitHub Actions workflow (.github/workflows/deploy.yml)
        ├─ Build & Test (.NET 8)
        ├─ azure/login@v2 (OIDC, no secrets to rotate)
        └─ azure/webapps-deploy@v3
              └─► Azure App Service (avolon-web)
                    ├─ Linux + .NET 8
                    ├─ WebSockets enabled (for SignalR)
                    └─ HTTPS only
```

## Files Created

| File | Purpose |
|---|---|
| `infra/main.bicep` | Bicep template to provision App Service Plan + Web App |
| `.github/workflows/deploy.yml` | CI/CD pipeline: build, test, deploy on push to `main` |

---

## Step-by-Step Setup

### Step 1 — Create Azure AD App Registration

```bash
az ad app create --display-name "avolon-web-deploy"
```

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "appId": "8fc566f7-a49c-4714-8663-7c17309f5c10",
  "id": "7a48bbfb-7736-453b-b03d-a57de38a9c08",
  "displayName": "avolon-web-deploy",
  "createdDateTime": "2026-04-04T20:28:09Z",
  "signInAudience": "AzureADMyOrg"
}
```

</details>

**Key values to note:**
- `appId` (`8fc566f7-a49c-4714-8663-7c17309f5c10`) — used as `AZURE_CLIENT_ID` in GitHub Secrets
- `id` (`7a48bbfb-7736-453b-b03d-a57de38a9c08`) — the object ID, used in the federated credential creation (Step 4)

**Status:** ✅ Done

---

### Step 2 — Create Service Principal

```bash
az ad sp create --id 8fc566f7-a49c-4714-8663-7c17309f5c10
```

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "accountEnabled": true,
  "appDisplayName": "avolon-web-deploy",
  "appId": "8fc566f7-a49c-4714-8663-7c17309f5c10",
  "appOwnerOrganizationId": "604c4563-431d-4538-8339-e22291a6afb0",
  "id": "d7794319-68dc-4bb3-ab80-9de3b34b1d63",
  "servicePrincipalType": "Application",
  "signInAudience": "AzureADMyOrg"
}
```

</details>

**Status:** ✅ Done

---

### Step 3 — Assign Contributor Role

```bash
az role assignment create \
  --assignee 8fc566f7-a49c-4714-8663-7c17309f5c10 \
  --role Contributor \
  --scope /subscriptions/f3eecfde-cfdc-4b28-b997-c3cd03854318/resourceGroups/avolon-web
```

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "name": "cebd79c9-021d-4451-a8c2-6e41896e1a49",
  "principalId": "d7794319-68dc-4bb3-ab80-9de3b34b1d63",
  "principalType": "ServicePrincipal",
  "scope": "/subscriptions/f3eecfde-cfdc-4b28-b997-c3cd03854318/resourceGroups/avolon-web",
  "roleDefinitionId": ".../roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c"
}
```

</details>

**Status:** ✅ Done

---

### Step 4 — Add Federated Credential for GitHub Actions

Use the **object `id`** (not `appId`) from Step 1.

> **Note:** In PowerShell, passing inline JSON with escaped quotes often fails. Use a JSON file instead.

Create `infra/federated-credential.json`:

```json
{
  "name": "github-deploy",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Yaming-Hub/avalon-web:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}
```

Then run:

```bash
az ad app federated-credential create \
  --id 7a48bbfb-7736-453b-b03d-a57de38a9c08 \
  --parameters infra/federated-credential.json
```

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "id": "015e110b-9b02-4d80-a128-5257eee12680",
  "name": "github-deploy",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Yaming-Hub/avalon-web:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}
```

</details>

**Status:** ✅ Done

---

### Step 5 — Configure GitHub Secrets

Go to **GitHub repo → Settings → Secrets and variables → Actions → Repository secrets** and add:

| Secret Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | `8fc566f7-a49c-4714-8663-7c17309f5c10` |
| `AZURE_TENANT_ID` | `604c4563-431d-4538-8339-e22291a6afb0` |
| `AZURE_SUBSCRIPTION_ID` | `f3eecfde-cfdc-4b28-b997-c3cd03854318` |

> Use **repository secrets** (not environment secrets) since the workflow does not use GitHub environments.

**Status:** ✅ Done

---

### Step 6 — Provision Azure Infrastructure (one-time)

```powershell
az deployment group create `
  --resource-group avolon-web `
  --template-file infra/main.bicep `
  --parameters location=centralus `
  --name "avolon-web-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```

> **Note:** The resource group is in Central US. A unique deployment name with timestamp is used to avoid conflicts.

This creates:
- **App Service Plan** (`avolon-web-plan`): Linux, F1 (Free) SKU, Central US
- **Web App** (`avolon-web`): .NET 8, WebSockets enabled, HTTPS only

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "provisioningState": "Succeeded",
  "outputs": {
    "webAppName": { "value": "avolon-web" },
    "webAppUrl": { "value": "https://avolon-web.azurewebsites.net" }
  },
  "parameters": {
    "appName": { "value": "avolon-web" },
    "location": { "value": "centralus" },
    "sku": { "value": "F1" }
  }
}
```

</details>

**Status:** ✅ Done

---

### Step 7 — Trigger Deployment

Push any change to the `main` branch, or manually trigger via **GitHub → Actions → Build and Deploy → Run workflow**.

**Status:** ⬜ Pending

---

## Post-Deployment Verification

1. Visit `https://avolon-web.azurewebsites.net`
2. Check Swagger UI at `https://avolon-web.azurewebsites.net/swagger` (dev only)
3. Verify SignalR hub connectivity at `/hubs/game`

## Scaling Considerations

- **Single instance**: Current in-memory `GameRepository` works as-is.
- **Multiple instances**: Add [Azure SignalR Service](https://learn.microsoft.com/en-us/azure/azure-signalr/) as a backplane and replace `InMemoryGameRepository` with a distributed store (Redis or database).
