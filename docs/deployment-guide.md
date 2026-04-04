# Avalon Web — Azure App Service Deployment Guide

This document captures the full setup process for deploying the Avalon Web application to Azure App Service using GitHub Actions with OIDC authentication.

## Overview

| Item | Value |
|---|---|
| **Azure Resource Group** | `avolon-web` |
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
  "appId": "<APP_ID>",
  "id": "<OBJECT_ID>",
  "displayName": "avolon-web-deploy",
  "createdDateTime": "2026-04-04T20:28:09Z",
  "signInAudience": "AzureADMyOrg"
}
```

</details>

**Key values to note:**
- `appId` — used as `AZURE_CLIENT_ID` in GitHub Secrets
- `id` — the object ID, used in the federated credential creation (Step 4)

**Status:** ✅ Done

---

### Step 2 — Create Service Principal

```bash
az ad sp create --id <APP_ID>
```

<details>
<summary>Response (abbreviated)</summary>

```json
{
  "accountEnabled": true,
  "appDisplayName": "avolon-web-deploy",
  "appId": "<APP_ID>",
  "id": "<SP_OBJECT_ID>",
  "servicePrincipalType": "Application",
  "signInAudience": "AzureADMyOrg"
}
```

</details>

**Status:** ✅ Done

---

### Step 3 — Assign Contributor Role

Find your subscription ID first:

```bash
az account show --query id -o tsv
```

Then assign the role:

```bash
az role assignment create \
  --assignee <APP_ID> \
  --role Contributor \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/avolon-web
```

**Status:** ⬜ Pending

---

### Step 4 — Add Federated Credential for GitHub Actions

Use the **object `id`** (not `appId`) from Step 1:

```bash
az ad app federated-credential create \
  --id <OBJECT_ID> \
  --parameters "{\"name\":\"github-deploy\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:Yaming-Hub/avalon-web:ref:refs/heads/main\",\"audiences\":[\"api://AzureADTokenExchange\"]}"
```

**Status:** ⬜ Pending

---

### Step 5 — Configure GitHub Secrets

Go to **GitHub repo → Settings → Secrets and variables → Actions** and add:

| Secret Name | How to get the value |
|---|---|
| `AZURE_CLIENT_ID` | The `appId` from Step 1 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |

**Status:** ⬜ Pending

---

### Step 6 — Provision Azure Infrastructure (one-time)

```bash
az deployment group create \
  --resource-group avolon-web \
  --template-file infra/main.bicep
```

This creates:
- **App Service Plan** (`avolon-web-plan`): Linux, B1 SKU
- **Web App** (`avolon-web`): .NET 8, WebSockets enabled, HTTPS only

**Status:** ⬜ Pending

---

### Step 7 — Trigger Deployment

Push any change to the `main` branch, or manually trigger via **GitHub → Actions → Build and Deploy → Run workflow**.

---

## Post-Deployment Verification

1. Visit `https://avolon-web.azurewebsites.net`
2. Check Swagger UI at `https://avolon-web.azurewebsites.net/swagger` (dev only)
3. Verify SignalR hub connectivity at `/hubs/game`

## Scaling Considerations

- **Single instance**: Current in-memory `GameRepository` works as-is.
- **Multiple instances**: Add [Azure SignalR Service](https://learn.microsoft.com/en-us/azure/azure-signalr/) as a backplane and replace `InMemoryGameRepository` with a distributed store (Redis or database).
