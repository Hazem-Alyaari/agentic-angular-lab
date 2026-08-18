# Phase 4 — Server-side tool calling

## What changed from Phase 3

Phase 3 was streamed LLM chat: one LLM request produced text events, then
`RUN_FINISHED`.

Phase 4 is agentic: the model may request a tool, the backend executes it,
the result is sent back to the model, and the same AG-UI run continues until
a final natural-language answer.

```text
User
 ↓
Angular
 ↓
AG-UI
 ↓
AgentRunService
 ↓
LLM
 ↓
Tool Call
 ↓
ToolRegistry
 ↓
GetLeaveBalanceTool
 ↓
Result
 ↓
LLM
 ↓
Final Answer
 ↓
AG-UI
 ↓
Angular
```

## LLM tool calling vs AG-UI tool events

These are separate:

- **LLM tool calling** is the model outputting a function name and JSON
  arguments (OpenAI `tool_calls`).
- **AG-UI tool events** tell the frontend that a tool started, received
  argument deltas, ended, and produced a result.

`OpenAiLlmProvider` never emits AG-UI events. `AgentRunService` never sees
OpenAI SDK types. Angular never sees OpenAI.

## Provider-neutral `LlmStreamEvent`

`ILlmProvider` now yields:

- `LlmTextDelta`
- `LlmToolCallStarted`
- `LlmToolCallArgumentsDelta`
- `LlmToolCallCompleted`

That mapping exists so a later Claude/Gemini/local provider can reuse the same
agent loop.

## ToolRegistry

Tools are registered explicitly in `Program.cs`:

- `get_employee`
- `get_leave_balance`

No assembly scanning. Unknown names return an error JSON to the model.

## One AG-UI run, multiple LLM requests

`RUN_STARTED` is emitted once. If the first LLM turn requests tools, the
service executes them, appends `role: tool` messages, and calls the LLM again.
`RUN_FINISHED` is emitted only when a turn produces no further tool calls.

## Tool results

Two representations:

- **AG-UI:** `TOOL_CALL_RESULT` (`messageId`, `toolCallId`, `content`, `role: tool`)
- **LLM:** `LlmMessage` with `Role = "tool"` and `ToolCallId` (OpenAI
  `ToolChatMessage`)

## Max iteration protection

`MaxToolIterations = 5`. Agents can loop (tool → model → tool). A hard cap
prevents infinite spend/hangs if the model keeps calling tools.

If exceeded: `RUN_ERROR` with code `max_tool_iterations`.

## Cancellation

HTTP `CancellationToken` flows through `AgentRunService` → `StreamAsync` →
`ExecuteAsync`. Disconnecting the browser stops further LLM/tool work.

## Errors

| Case | Behavior |
|---|---|
| Missing messages | HTTP 400 |
| LLM failure | `RUN_ERROR` / `llm_error` |
| Unknown tool | error JSON back to the model as a tool result |
| Malformed arguments | error JSON from the tool |
| Employee not found | `{ "error": "Employee not found" }` then LLM explains |
| Too many iterations | `RUN_ERROR` / `max_tool_iterations` |

No stack traces or API keys are returned.

## Intentionally not implemented

- frontend tools / Angular Router actions
- generative UI, shared state, HITL
- RAG, MCP, LangGraph, CopilotKit
- databases, auth, real HR APIs
