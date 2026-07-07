# Helpdesk Copilot — Microsoft Foundry PoC

A small, educational chatbot that demonstrates all four core Microsoft Foundry building blocks: **model**, **agent**, **tools**, and **knowledge base**. See [PRD.md](PRD.md) for the full product spec.

## What you'll learn

| Foundry Pillar | Where it lives in this repo |
|---|---|
| **Model** | A chat-completion model deployed in your Foundry project (`infra/foundry.bicep`), referenced by name in `Agent:ModelDeploymentName`. |
| **Agent** | [`src/Helpdesk.Agent/HelpdeskAgentService.cs`](src/Helpdesk.Agent/HelpdeskAgentService.cs) — creates a Persistent Agent, manages threads/runs. |
| **Tools** | [`src/Helpdesk.Agent/ToolDefinitions.cs`](src/Helpdesk.Agent/ToolDefinitions.cs) — a function tool wired to a real REST API ([`src/Helpdesk.Api`](src/Helpdesk.Api)). |
| **Knowledge base** | [`docs/knowledge-base/`](docs/knowledge-base) — markdown docs uploaded to a File Search vector store for grounded, cited answers. |

## Architecture

The app is orchestrated by a .NET Aspire AppHost ([`src/Helpdesk.AppHost`](src/Helpdesk.AppHost)), which drives both the local
dev inner loop (dashboard, service discovery, health checks, OpenTelemetry) and Azure deployment (via `azd`'s native
Aspire integration — Container Apps, Container Registry, and the Container Apps Environment are generated from the
app model, not hand-written Bicep).

```
Browser (chat UI)
     │
     ▼
Helpdesk.Web  /api/chat ──► Foundry Agent (model + File Search + function tool)
     │                                            │
     └──────────────── calls (service discovery) ►│
                                                    ▼
                                            Helpdesk.Api (mock tickets & leave balance)
```

See [docs/architecture.md](docs/architecture.md) for a detailed component diagram plus sequence diagrams for agent bootstrap, grounded Q&A (with citations), and tool-driven ticket creation.

## Prerequisites

- .NET 10 SDK (includes the Aspire app host/service defaults templates)
- A container runtime (Docker Desktop or Podman) — Aspire uses this for local orchestration
- Azure subscription + [`az`](https://aka.ms/installazurecli) and [`azd`](https://aka.ms/azure-dev/install) CLIs, logged in (`az login`, `azd auth login`)

## Run locally

```powershell
dotnet run --project src/Helpdesk.AppHost
```

This opens the Aspire dashboard, starts `Helpdesk.Api` and `Helpdesk.Web` with service discovery wired between them, and
provisions/connects to the Foundry resources declared in `infra/foundry.bicep` (using your `az login` session — no
local emulator exists for Foundry Agent Service, so this still talks to a real Foundry project). Open the `web`
endpoint shown in the dashboard and try:
- *"How many vacation days do I get?"*
- *"What's the status of ticket #123?"*
- *"Reset my laptop password, please open a ticket for alice."*

## Run tests

```powershell
dotnet test HelpdeskCopilot.slnx
```

## Deploy to Azure

```powershell
azd auth login
azd up
```

This provisions (dynamically generated from `src/Helpdesk.AppHost/AppHost.cs`, plus `infra/foundry.bicep` for the
Foundry-specific resources):
- A Microsoft Foundry account + project + model deployment
- Azure Container Apps environment + Azure Container Registry
- Two Container Apps: `api` (internal-only — it has no end-user auth, so it's not reachable from the public internet) and `web` (public ingress, granted **Cognitive Services User** on the Foundry account via a dedicated user-assigned managed identity — no keys/secrets)

When it finishes, `azd` prints the `web` app's public URL — open it and run the same test scenarios as above, now live on Azure.

## Cleanup

```powershell
azd down
```

Deletes every resource `azd up` created (Foundry account, model deployment, Container Apps, ACR, Log Analytics).

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Helpdesk.Web` throws on startup about `ProjectEndpoint` | Set `Agent:ProjectEndpoint` / `Agent:ModelDeploymentName` in config, env vars, or via the AppHost's `foundry` Bicep resource outputs. |
| Chat replies with "Sorry, I couldn't process that request" | Check `Helpdesk.Web` console logs (or the Aspire dashboard's structured logs/traces) — usually a missing RBAC role or a model deployment that doesn't exist yet. |
| `azd up` fails on model deployment quota | Pick a different region/model via the `modelName`/`modelVersion` params in `infra/foundry.bicep`, or check quota with the Foundry quota tooling. |
| Local run can't reach `Helpdesk.Api` | Run via `dotnet run --project src/Helpdesk.AppHost` (not the individual projects) so Aspire's service discovery wires `Agent:HelpdeskApiBaseUrl` (`http://api`) to the actual dynamic port. |

## Repo structure

```
PRD.md                        Product requirements
azure.yaml                    azd service definition (points at the AppHost)
infra/foundry.bicep           Bicep: Foundry account + project + model deployment + RBAC
docs/knowledge-base/          Knowledge base markdown docs (File Search)
src/Helpdesk.AppHost/         Aspire orchestrator - local dev loop + Azure deployment driver
src/Helpdesk.ServiceDefaults/ Shared OpenTelemetry/health-checks/service-discovery wiring
src/Helpdesk.Api/             Mock ticketing + leave-balance REST API
src/Helpdesk.Agent/           Foundry SDK code: agent, tools, knowledge base
src/Helpdesk.Web/             Chat UI + /api/chat bridge
```
