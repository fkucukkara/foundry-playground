# Architecture, Flow & Sequence Diagrams

Companion to [PRD.md](../PRD.md) and [README.md](../README.md). These diagrams reflect the
actual implementation in `src/` as of this review, not just the PRD's aspirational design.

## 1. Component / Flow Diagram

```mermaid
flowchart TB
    subgraph Browser
        UI["Chat UI (wwwroot/index.html)"]
    end

    subgraph "Helpdesk.AppHost (orchestrator)"
        AppHost["Local dev dashboard /
        Azure deployment driver
        (azd generates infra from this app model)"]
    end

    subgraph "Helpdesk.Web (Container App, public ingress)"
        ChatEndpoint["POST /api/chat<br/>(rate-limited: 10 req/min/IP,<br/>4000-char message cap)"]
    end

    subgraph "Helpdesk.Agent (class library)"
        AgentSvc["HelpdeskAgentService<br/>EnsureAgentAsync / ChatAsync"]
        Tools["ToolDefinitions<br/>get_leave_balance, get_ticket_status,<br/>list_tickets, create_ticket"]
        ApiClient["HelpdeskApiClient (HttpClient,<br/>via Microsoft.Extensions.ServiceDiscovery)"]
    end

    subgraph "Microsoft Foundry"
        Model["Chat model deployment<br/>(gpt-4o-mini)"]
        FileSearch["File Search tool<br/>+ Vector Store"]
        AgentDef["Persistent Agent<br/>'helpdesk-copilot'"]
    end

    subgraph "Helpdesk.Api (Container App, internal-only ingress)"
        Endpoints["/api/tickets, /api/tickets/id,<br/>/api/leave-balance/id"]
        Store["HelpdeskStore<br/>(in-memory, validated input)"]
    end

    KB["docs/knowledge-base/*.md<br/>(baked into web build/publish output)"]

    AppHost -.->|"orchestrates + wires service discovery"| ChatEndpoint
    AppHost -.->|"orchestrates"| Endpoints
    UI -->|"HTTP POST {sessionId, message}"| ChatEndpoint
    ChatEndpoint --> AgentSvc
    AgentSvc -->|"create/reuse thread, run"| AgentDef
    AgentDef --> Model
    AgentDef -->|"grounds answer"| FileSearch
    FileSearch -.->|"indexed at first run"| KB
    AgentDef -->|"RequiresAction: tool call"| AgentSvc
    AgentSvc --> Tools
    Tools --> ApiClient
    ApiClient -->|"REST via service discovery (http://api)"| Endpoints
    Endpoints --> Store
    ApiClient -.->|"tool output"| AgentDef
    AgentDef -->|"final message +<br/>file citations"| AgentSvc
    AgentSvc --> ChatEndpoint
    ChatEndpoint -->|"{reply}"| UI
```

**Security-relevant notes reflected above (see review in chat for full rationale):**
- `Helpdesk.Api` ingress is **internal-only** — it has no end-user auth, so it must not be reachable from the public internet; only `Helpdesk.Web` calls it, over the Container Apps environment's internal network. The base URL is resolved via Aspire service discovery (`http://api`, wired by `.WithReference(api)` in `Helpdesk.AppHost`) rather than a hardcoded address, so it works identically for local dev (dynamic port) and Azure (internal Container Apps DNS).
- `Helpdesk.Web` → Foundry uses a **dedicated user-assigned managed identity** (`builder.AddAzureUserAssignedIdentity(...)` in `Helpdesk.AppHost`, attached via `.WithAzureUserAssignedIdentity(...)`), authenticated with `DefaultAzureCredential` — no API keys. `disableLocalAuth: true` on the Cognitive Services account (`infra/foundry.bicep`) enforces this.
- `/api/chat` is rate-limited and input-length-capped to bound cost/abuse exposure given there's no per-user authentication (PRD non-goal).
- `Helpdesk.ServiceDefaults` (referenced by both `Helpdesk.Api` and `Helpdesk.Web`) wires OpenTelemetry (traces/metrics/logs), `/health` + `/alive` health checks, and service discovery/resilience HTTP handlers consistently across both services.

## 2. Sequence Diagram — Agent Bootstrap (first request after cold start)

```mermaid
sequenceDiagram
    participant Web as Helpdesk.Web
    participant Svc as HelpdeskAgentService
    participant Foundry as Foundry (Administration/Files/VectorStores)

    Web->>Svc: ChatAsync(sessionId, message)
    Svc->>Svc: EnsureAgentAsync()
    activate Svc
    Svc->>Foundry: GetAgentsAsync()
    alt agent "helpdesk-copilot" already exists
        Foundry-->>Svc: existing agent id
    else no agent yet
        loop for each *.md in docs/knowledge-base
            Svc->>Foundry: Files.UploadFileAsync(file, purpose=Agents)
            Foundry-->>Svc: fileId
        end
        Svc->>Foundry: VectorStores.CreateVectorStoreAsync(fileIds)
        Foundry-->>Svc: vectorStoreId
        Svc->>Foundry: Administration.CreateAgentAsync(model, tools=[4 functions + FileSearch], toolResources=vectorStoreId)
        Foundry-->>Svc: new agent id
    end
    deactivate Svc
    Svc->>Svc: cache agentId (in-memory, singleton lifetime)
```

## 3. Sequence Diagram — Grounded Policy Question (File Search + citations)

*User: "How many vacation days do I get?"*

```mermaid
sequenceDiagram
    actor User
    participant UI as Chat UI
    participant Web as Helpdesk.Web /api/chat
    participant Svc as HelpdeskAgentService
    participant Run as Foundry Run
    participant FS as File Search / Vector Store

    User->>UI: types question
    UI->>Web: POST {sessionId, message}
    Web->>Web: validate (non-empty, <=4000 chars) + rate limit
    Web->>Svc: ChatAsync(sessionId, message)
    Svc->>Svc: GetOrCreateThreadAsync(sessionId)
    Svc->>Run: Messages.CreateMessageAsync(threadId, user, message)
    Svc->>Run: Runs.CreateRunAsync(threadId, agentId)
    Run->>FS: search knowledge base for grounding
    FS-->>Run: relevant chunks + file citations
    loop poll until terminal status (max 90s, then friendly timeout)
        Svc->>Run: Runs.GetRunAsync(threadId, runId)
        Run-->>Svc: status (queued/in_progress/completed)
    end
    Svc->>Run: Messages.GetMessagesAsync(threadId, desc)
    Run-->>Svc: assistant message + MessageTextFileCitationAnnotation[]
    Svc->>Svc: map fileId -> filename, append "Sources: hr-policy-leave-types.md"
    Svc-->>Web: reply text + sources
    Web-->>UI: {reply}
    UI-->>User: shows grounded, cited answer
```

## 4. Sequence Diagram — Action via Function Tool (create a ticket)

*User: "Reset my laptop password, please open a ticket for alice."*

```mermaid
sequenceDiagram
    actor User
    participant UI as Chat UI
    participant Web as Helpdesk.Web /api/chat
    participant Svc as HelpdeskAgentService
    participant Run as Foundry Run
    participant Tools as ToolDefinitions.InvokeAsync
    participant Api as Helpdesk.Api

    User->>UI: types request
    UI->>Web: POST {sessionId, message}
    Web->>Svc: ChatAsync(sessionId, message)
    Svc->>Run: CreateMessageAsync + CreateRunAsync
    Run-->>Svc: status = requires_action (SubmitToolOutputsAction)
    Svc->>Tools: InvokeAsync("create_ticket", {userId, category, subject, description})
    Tools->>Api: POST /api/tickets (internal ingress)
    Api->>Api: CreateTicketRequest.Validate() (category, lengths, required fields)
    alt validation fails
        Api-->>Tools: 400 { errors: [...] }
    else valid
        Api->>Api: HelpdeskStore.CreateTicket (id counter starts at 1000, never collides with seed ticket "123")
        Api-->>Tools: 201 { ticketId, status: "Open" }
    end
    Tools-->>Svc: tool output JSON
    Svc->>Run: Runs.SubmitToolOutputsToRunAsync(run, outputs)
    loop poll until completed
        Svc->>Run: GetRunAsync
    end
    Svc->>Run: GetMessagesAsync
    Run-->>Svc: assistant confirms new ticket id
    Svc-->>Web: reply text
    Web-->>UI: {reply}
    UI-->>User: "Ticket #1001 created..."
```
