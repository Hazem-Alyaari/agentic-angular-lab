# Phase 2 — AG-UI streaming (no LLM)

## What AG-UI is

AG-UI (Agent-User Interaction Protocol) is an open, event-based protocol for
connecting frontends to agent backends. The client sends a `RunAgentInput`
payload over HTTP POST. The server responds with a stream of typed JSON events
(commonly as Server-Sent Events).

## What we implemented

- `POST /api/agent/run` on ASP.NET Core emits a hardcoded AG-UI event stream
- Angular posts a `RunAgentInput` and consumes SSE frames one event at a time
- The UI appends text only when `TEXT_MESSAGE_CONTENT` arrives
- No LLM, CopilotKit, tools, state sync, or generative UI

## Request flow

```text
Angular AgUiService
  POST /api/agent/run  (RunAgentInput JSON)
        ↓
ASP.NET Core
  text/event-stream
        ↓
SSE frames: data: {type, ...}\n\n
        ↓
EventSchemas.parse (from @ag-ui/core)
        ↓
AppComponent.handleEvent → UI
```

## Event lifecycle (Phase 2)

1. `RUN_STARTED` — status becomes `running`
2. `TEXT_MESSAGE_START` — open a new assistant message buffer
3. `TEXT_MESSAGE_CONTENT` — append each `delta` (`Hello` / ` from` / ` AG-UI`)
4. `TEXT_MESSAGE_END` — assistant message is complete
5. `RUN_FINISHED` — status becomes `idle`

Final assembled text: `Hello from AG-UI`

## What surprised us

- Official `@ag-ui/client` `HttpAgent` works, but it also owns message/state
  bookkeeping. For learning the protocol, `@ag-ui/core` + a thin `fetch` SSE
  reader made the lifecycle more visible.
- ASP.NET Core `TypedResults.ServerSentEvents` plus `AGUI.Abstractions` types is
  enough for a compliant wire format without `AGUI.Server` / `IChatClient`.

## What is still missing

- Real agent / LLM
- Tool calling events
- Shared state (`STATE_SNAPSHOT` / `STATE_DELTA`)
- Generative UI
- Human-in-the-loop interrupts
- Message history persistence

## Why this phase does not use an LLM

Phase 2 is about understanding the protocol transport and event semantics.
A fixed stream proves the client can parse and render AG-UI events correctly
before introducing model latency, failures, and tool behavior.
