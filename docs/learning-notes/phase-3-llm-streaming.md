# Phase 3 — LLM streaming through AG-UI

## Architecture before

```text
Angular → RunAgentInput → POST /api/agent/run → hardcoded TEXT_MESSAGE_CONTENT → Angular
```

## Architecture after

```text
Angular
  → RunAgentInput
  → POST /api/agent/run
  → AgentRunService
  → ILlmProvider.StreamAsync
  → OpenAI (or compatible) token stream
  → TEXT_MESSAGE_CONTENT events
  → Angular
```

## Why `ILlmProvider` exists

`AgentRunService` must stay provider-agnostic. The interface returns plain text
chunks only. Swapping OpenAI for another compatible backend should not require
changing AG-UI event construction.

## Why the provider does not emit AG-UI events

Protocol ownership stays in one place (`AgentRunService`). Providers know models
and SDKs; they should not know `RUN_STARTED` / `TEXT_MESSAGE_*` / `RUN_FINISHED`.

## Message mapping

From `RunAgentInput.messages`, in order:

| AG-UI message | LLM role |
|---|---|
| `system` | `system` |
| `user` (string or text parts) | `user` |
| `assistant` | `assistant` |

Tool / activity / reasoning messages are ignored in Phase 3.

Empty usable message lists are rejected with HTTP 400 before SSE starts.

## Chunk → event mapping

1. `RUN_STARTED`
2. `TEXT_MESSAGE_START`
3. each provider chunk → `TEXT_MESSAGE_CONTENT.delta`
4. `TEXT_MESSAGE_END`
5. `RUN_FINISHED`

On provider failure after the run started: `TEXT_MESSAGE_END` then `RUN_ERROR`
(no `RUN_FINISHED`).

## Cancellation

The HTTP request `CancellationToken` is passed into `AgentRunService` and
`ILlmProvider.StreamAsync`. Aborting the browser fetch stops reading the OpenAI
stream via `CompleteChatStreamingAsync(..., cancellationToken)`.

## Configuration / secrets

Non-secret defaults live in `appsettings.json` (`Llm:Model`).

Set the API key with user-secrets (recommended):

```bash
cd backend/Agent.Api
dotnet user-secrets set "Llm:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Llm:Model" "gpt-4o-mini"
# optional OpenAI-compatible endpoint:
dotnet user-secrets set "Llm:BaseUrl" "https://api.openai.com/v1"
```

Or environment variables:

```text
LLM_API_KEY
LLM_MODEL
LLM_BASE_URL
```

Also supported: `Llm__ApiKey`, `Llm__Model`, `Llm__BaseUrl`.

Never commit real keys. Angular never sees provider credentials.

## Intentionally not implemented

- tool calling
- MCP / LangGraph / CopilotKit
- RAG / vector DB
- auth
- shared state / generative UI
- multi-provider runtime switching UI
