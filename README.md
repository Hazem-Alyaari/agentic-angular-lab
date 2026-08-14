# Agentic Angular Lab

Agentic Angular Lab is a learning-focused project for exploring agentic AI patterns with Angular, ASP.NET Core, and AG-UI.

This repository is intentionally starting small. The goal is to learn one layer at a time, with a working foundation before adding agent protocols, tools, or model providers.

## Current Status

Phase 1 is in place: a local Angular frontend can call a simple ASP.NET Core Web API.

What works today:

- Angular 20 application under `frontend/angular-app`
- ASP.NET Core Web API under `backend/Agent.Api`
- `GET /api/health` returns `{ "status": "ok" }`
- Local development proxy so the Angular app can reach the API

This is a connectivity foundation only. It is not an agent yet.

## Planned Learning Areas

These are not implemented yet. The project will gradually explore:

- AG-UI
- streaming
- agent communication
- tool calling
- frontend tools
- shared state
- structured/generative UI
- human-in-the-loop workflows

## Prerequisites

- Node.js 22
- Angular CLI 20
- .NET 10 SDK

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
├── docs/                     Notes and screenshots
├── README.md
└── .gitignore
```
