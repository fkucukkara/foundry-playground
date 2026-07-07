# PRD: Helpdesk Copilot — Microsoft Foundry PoC

## 1. Summary

**Helpdesk Copilot** is a small, educational proof-of-concept chatbot built to learn the core capabilities of **Microsoft Foundry** hands-on: deploying a **model**, building an **agent**, giving it **tools** (function calling against a real API), and grounding it with a **knowledge base** (file search / RAG). It answers internal IT & HR questions and can take simple actions (create a support ticket, check ticket status, check leave balance).

The PoC is intentionally small: one agent, one model, one knowledge base, one custom tool, one chat UI. It is designed to run locally for fast iteration, and to deploy to Azure with a single command for live testing.

### Learning Objectives

| Foundry Pillar | What this PoC demonstrates |
|---|---|
| **Model** | Deploying and calling a chat-completion model through a Foundry project. |
| **Agent** | Creating a Persistent Agent in code (instructions, tool bindings, thread/run lifecycle). |
| **Tools** | A custom function tool that calls a real REST API to take action (create/query tickets, check leave balance). |
| **Knowledge base** | Grounding answers in a small set of policy documents via Foundry's built-in File Search (vector store), including citations. |

## 2. Problem Statement / Motivation

The user (a Chief Software Engineer) wants a concrete, runnable example that touches every major Foundry building block without the overhead of a real production system. Most tutorials cover only one capability (just a model, or just RAG); this PoC intentionally combines all four in one small, understandable codebase.

## 3. Goals

- G1: A chat agent that answers IT/HR policy questions, grounded in real documents (with citations).
- G2: The same agent can call a real tool/API to perform an action (not just talk about it).
- G3: A minimal web chat UI to interact with the agent.
- G4: Runs locally end-to-end for fast iteration.
- G5: Deploys to Azure with a single command (`azd up`) and is testable live over a public URL.
- G6: Code and docs make it obvious *which Foundry concept* each piece demonstrates (learning-first).

### Non-Goals

- Authentication/authorization for end users of the chat UI.
- Multi-agent orchestration or agent hand-offs.
- Model fine-tuning or prompt optimization workflows.
- Streaming token-by-token responses (nice-to-have, not required).
- Production hardening (rate limiting, WAF, multi-region, DR).
- Integration with a real ticketing/HR system (mock data only).
- CI/CD pipelines.

## 4. Target User / Persona

A single internal employee asking common IT/HR questions through a simple web chat, e.g. "How many vacation days do I have left?" or "My laptop needs a password reset, please open a ticket."

## 5. Scope

**In scope:**
- 1 Foundry project, 1 chat model deployment.
- 1 agent ("Helpdesk Copilot") with system instructions.
- 1 File Search tool grounded in 5 markdown policy documents.
- 1 function tool wrapping a small custom REST API (list/create tickets, ticket status, leave balance).
- 1 minimal web chat UI + thin backend bridge.
- Local run instructions and a one-command Azure deployment.

**Out of scope:** see Non-Goals above.

## 6. Architecture Overview

```
Browser (chat UI)
     │  HTTP
     ▼
Helpdesk.Web  (ASP.NET Core minimal API + static chat page)
  - /api/chat  → creates/reuses a Foundry agent thread per session,
                 posts the user message, runs the agent, returns the reply
     │
     ▼
Foundry Agent  ("helpdesk-copilot")
  - Model: chat-completion deployment in the Foundry project
  - Tool 1: File Search  → vector store built from docs/knowledge-base/*.md
  - Tool 2: Function tool → calls Helpdesk.Api
     │
     ▼
Helpdesk.Api  (ASP.NET Core minimal API, mock in-memory data)
  - GET  /api/tickets?userId=
  - POST /api/tickets
  - GET  /api/tickets/{ticketId}
  - GET  /api/leave-balance/{userId}
```

`Helpdesk.Agent` is a shared class library (referenced by `Helpdesk.Web`) that contains the Foundry SDK code: creating/ensuring the agent, uploading the knowledge base to a File Search vector store, registering the function tool, and running threads. Keeping this as explicit code (rather than a CLI-generated scaffold) keeps every Foundry SDK call visible and easy to learn from.

## 7. Functional Requirements

### 7.1 Model
- One chat-completion model deployed in the Foundry project (e.g. a GPT-4o-class model). Configurable via `FOUNDRY_PROJECT_ENDPOINT` and `MODEL_DEPLOYMENT_NAME`.

### 7.2 Agent
- Single Persistent Agent, name `helpdesk-copilot`.
- System instructions: helpful IT/HR assistant; must use the File Search tool before answering policy questions and cite the source document; must use the function tool for any ticket/leave-balance action rather than guessing; must ask for a `userId` if one is needed and not yet known.
- Agent and its threads are created/ensured in code at `Helpdesk.Web` startup / first request (idempotent — reuse if it already exists).

### 7.3 Tools
- **File Search tool** — read-only grounding over the knowledge base vector store.
- **Function tool** — exposes 4 operations against `Helpdesk.Api`:
  - `GetLeaveBalance(userId)`
  - `CreateTicket(userId, category, subject, description)`
  - `GetTicketStatus(ticketId)`
  - `ListTickets(userId)`

### 7.4 Knowledge Base
Markdown documents (authored in `docs/knowledge-base/`):
- `it-policy-password-reset.md`
- `it-policy-hardware-request.md`
- `hr-policy-leave-types.md`
- `hr-policy-expense-reimbursement.md`
- `faq-general.md`

Uploaded to the agent's File Search vector store at startup (idempotent — skip if already indexed).

### 7.5 Chat UI
- Single HTML page (static, served by `Helpdesk.Web`) with a message list and input box.
- Posts to `/api/chat` with `{ sessionId, message }`; displays the assistant's reply.
- Session-to-thread mapping kept server-side (in-memory for the PoC).

## 8. API Design — `Helpdesk.Api`

| Method & Route | Request | Response | Notes |
|---|---|---|---|
| `GET /api/tickets?userId={id}` | — | `Ticket[]` | List tickets for a user |
| `POST /api/tickets` | `{userId, category, subject, description}` | `{ticketId, status}` | Category: `IT` or `HR` |
| `GET /api/tickets/{ticketId}` | — | `Ticket` | Ticket detail/status |
| `GET /api/leave-balance/{userId}` | — | `{vacationDays, sickDays}` | Mock balance |
| `GET /healthz` | — | `200 OK` | Health probe for Container Apps |

Data is in-memory, seeded with 1-2 demo users at startup. OpenAPI/Swagger UI enabled for discoverability.

## 9. Repo Structure

```
/PRD.md
/README.md
/azure.yaml
/docs/knowledge-base/*.md
/infra/                      (Bicep: Foundry account+project+model deployment,
                               Container Apps environment, ACR, the two container apps,
                               managed identities, RBAC role assignments)
/src/Helpdesk.Api/           (mock ticketing + leave-balance REST API)
/src/Helpdesk.Agent/         (class library: Foundry SDK code — agent, file search, function tool)
/src/Helpdesk.Web/           (chat UI + /api/chat, references Helpdesk.Agent)
```

## 10. Prerequisites / Assumptions

- Azure subscription with quota for a chat model deployment.
- `azd` and `az` CLI installed and logged in.
- .NET SDK installed (matching the solution's target framework).
- A Foundry project is provisioned by the same `azd up` (or an existing one can be pointed to via configuration) — README documents both paths.

## 11. Milestones / Phased Plan

1. **Phase 1** — Author knowledge base docs; build `Helpdesk.Api` (mock data, endpoints, health check, Swagger).
2. **Phase 2** — Build `Helpdesk.Agent` library (agent creation, File Search upload, function tool wired to `Helpdesk.Api`).
3. **Phase 3** — Build `Helpdesk.Web` chat UI + `/api/chat` bridge with per-session thread tracking.
4. **Phase 4** — Author `azure.yaml` + `infra/` (Foundry resources + two container apps + RBAC); `azd up` end-to-end.
5. **Phase 5** — Run the acceptance scenarios (Section 12) both locally and against the live Azure deployment.

## 12. Acceptance Criteria / Test Scenarios

Run once locally (`dotnet run`) and once against the live `azd up` deployment:

1. Ask *"How many vacation days do I get?"* → grounded answer citing the HR leave-types document.
2. Ask *"What's the status of ticket #123?"* → agent calls `GetTicketStatus` and returns live mock data.
3. Say *"Reset my laptop password, please open a ticket"* → agent calls `CreateTicket` and confirms a new ticket ID.
4. `azd down` cleanly removes all provisioned resources.

## 13. Risks & Open Questions

- Model/quota availability in the chosen Azure region.
- File Search tool is more than sufficient for this doc-count/size — no scaling concerns at PoC scale.
- RBAC role assignments may take a minute to propagate after `azd up`; a retry may be needed on the very first live test.
- No existing Foundry resource was found in the target subscription at planning time — `azd up` provisions one from scratch.

## 14. Future Enhancements (explicitly deferred)

- Streaming responses.
- Authentication for the chat UI.
- Multi-agent setup (separate IT vs. HR sub-agents).
- Real ticketing/HR system integration.
- Evaluation & continuous monitoring via Foundry's evaluation workflow.
- CI/CD pipeline.

## 15. README Requirements

The accompanying `README.md` must cover: prerequisites, one-time setup (`az login`, `azd auth login`), local run instructions per app, one-command Azure deployment (`azd up`) and what it provisions, how to test against the deployed URLs, cleanup (`azd down`), an architecture diagram, a "What you'll learn" recap tied to Section 1's Learning Objectives, and a troubleshooting section.
