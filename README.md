# Agentic Angular Lab

Agentic Angular Lab is a learning-focused project for exploring agentic AI patterns with Angular, ASP.NET Core, and AG-UI.

This repository is intentionally starting small. The goal is to learn one layer at a time, with a working foundation before adding agent protocols, tools, or model providers.

## Current Status

Phase 5 is in place: the model can call **server tools** (ASP.NET Core) and one **frontend tool** (Angular Router) over raw AG-UI.

What works today:

- Angular 20 application under `frontend/angular-app`
- ASP.NET Core Web API under `backend/Agent.Api`
- `GET /api/health` returns `{ "status": "ok" }`
- `POST /api/agent/run` streams AG-UI events from a real LLM
- Server-side tools `get_employee` and `get_leave_balance` over mock HR data
- Frontend tool `navigate_to_employee` advertised in `RunAgentInput.tools` and executed by Angular Router at `/employees/:id`
- Frontend tool results resume the agent through official AG-UI interrupt + tool messages (not a custom `/api/tool-result`)

There is still no generative UI, shared application state, HITL confirmation, RAG, or CopilotKit.

See:

- `docs/learning-notes/phase-2-ag-ui-streaming.md`
- `docs/learning-notes/phase-3-llm-streaming.md`
- `docs/learning-notes/phase-4-server-tool-calling.md`
- `docs/learning-notes/phase-5-frontend-tools.md`

## Planned Learning Areas

These are not implemented yet. The project will gradually explore:

- shared frontend context/state
- structured/generative UI
- human-in-the-loop workflows

## Prerequisites

- Node.js 22
- Angular CLI 20
- .NET 10 SDK
- An OpenAI API key (or OpenAI-compatible endpoint)

## Configuration

From `backend/Agent.Api`:

```bash
dotnet user-secrets set "Llm:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Llm:Model" "gpt-4o-mini"
# optional:
dotnet user-secrets set "Llm:BaseUrl" "https://api.openai.com/v1"
```

Environment variable alternatives: `LLM_API_KEY`, `LLM_MODEL`, `LLM_BASE_URL`.

## How to run

Start the backend first, then the frontend.

### Backend

```bash
cd backend/Agent.Api
dotnet run --launch-profile http
```

The API listens on `http://localhost:5177`.

Health check:

```text
GET http://localhost:5177/api/health
```

AG-UI run (SSE):

```text
POST http://localhost:5177/api/agent/run
Accept: text/event-stream
```

### Frontend

```bash
cd frontend/angular-app
npm start
```

The Angular app listens on `http://localhost:4200`.

During local development, requests to `/api/*` are proxied to `http://localhost:5177`.

## Repository layout

```text
agentic-angular-lab/
├── frontend/angular-app/     Angular application
├── backend/Agent.Api/        ASP.NET Core Web API
│   ├── Agents/               AG-UI run loop + tool ownership + ToolRegistry
│   ├── Llm/                  ILlmProvider + OpenAI implementation
│   ├── Tools/                get_employee, get_leave_balance
│   └── Data/                 in-memory mock employees
├── docs/                     Notes and screenshots
├── README.md
└── .gitignore
```
