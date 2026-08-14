# Agentic Angular Lab

Agentic Angular Lab is a learning-focused project for exploring agentic AI patterns with Angular, ASP.NET Core, and AG-UI.

This repository is intentionally starting small. The goal is to learn one layer at a time, with a working foundation before adding agent protocols, tools, or model providers.

## Current Status

Phase 3 is in place: Angular sends `RunAgentInput` over AG-UI SSE, and ASP.NET Core streams a real LLM response as AG-UI events.

What works today:

- Angular 20 application under `frontend/angular-app`
- ASP.NET Core Web API under `backend/Agent.Api`
- `GET /api/health` returns `{ "status": "ok" }`
- `POST /api/agent/run` streams AG-UI events from a real LLM (`ILlmProvider`)
- Angular parses each AG-UI event and renders assistant text incrementally
- Local development proxy so the Angular app can reach the API

There is still no tool calling, shared state, generative UI, RAG, or multi-agent orchestration.

See:

- `docs/learning-notes/phase-2-ag-ui-streaming.md`
- `docs/learning-notes/phase-3-llm-streaming.md`

## Planned Learning Areas

These are not implemented yet. The project will gradually explore:

- tool calling
- frontend tools
- shared state
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
│   ├── Agents/               AG-UI run lifecycle
│   └── Llm/                  ILlmProvider + OpenAI implementation
├── docs/                     Notes and screenshots
├── README.md
└── .gitignore
```
