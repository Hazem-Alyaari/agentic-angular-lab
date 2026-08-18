# Phase 5 — Frontend tools

## What changed from Phase 4

Phase 4 executed tools only on the server:

```text
LLM → get_leave_balance → ToolRegistry → ASP.NET Core → TOOL_CALL_RESULT → LLM
```

Phase 5 adds a **frontend tool**. The model can request a UI capability that
only Angular can perform. The backend must not fake that capability.

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
get_employee
 ↓
Backend ToolRegistry
 ↓
Employee 101
 ↓
LLM
 ↓
navigate_to_employee
 ↓
AG-UI
 ↓
Angular FrontendToolExecutor
 ↓
Router.navigate('/employees/101')
 ↓
tool result
 ↓
Agent
 ↓
LLM
 ↓
final response
```

## Server tools vs frontend tools

### Server tool

Example: `get_leave_balance`

```text
Backend
   ↓
ToolRegistry
   ↓
GetLeaveBalanceTool
```

The browser observes `TOOL_CALL_*` events. It does **not** execute the tool.
The trusted execution environment is ASP.NET Core. Validation/authorization
still belong there later; Phase 5 does not add auth.

### Frontend tool

Example: `navigate_to_employee`

The definition originates from Angular because it describes a capability of
the UI:

```text
Angular
   ↓
Angular Router
   ↓
/employees/:id
```

The backend cannot call Angular Router. Registering `navigate_to_employee` as
an `IAgentTool` would defeat this phase.

Why Angular advertises it: AG-UI's `RunAgentInput.tools` is specifically for
**client-provided** tools. Server tools stay in the backend `ToolRegistry`.
The LLM sees the merged set; execution ownership stays explicit.

## Official AG-UI continuation (what we inspected)

Installed versions:

- `@ag-ui/core` 0.0.58
- `AGUI.Abstractions` 0.0.5

Findings:

1. **`RunAgentInput.tools`** is the official place for frontend/client tools.
   Backend tools are not supposed to be copied into that array.

2. **Tool results** are AG-UI **tool messages**
   (`role: "tool"`, `toolCallId`, `content`) and/or `TOOL_CALL_RESULT` events.

3. HTTP SSE is **one-way**. The browser cannot push a tool result on the same
   open stream. Raw AG-UI therefore cannot keep one HTTP request waiting for
   Angular Router.

4. The official pause/resume mechanism in these versions is the
   **interrupt-aware run lifecycle**:
   - `RUN_FINISHED` with `outcome: { type: "interrupt", interrupts: [...] }`
   - next `RunAgentInput` on the same `threadId` carries `resume[]`
   - `MESSAGES_SNAPSHOT` before the interrupt gives the client the
     conversation needed to continue

5. Maintainer guidance for client tools (AG-UI issue #381): if a tool from
   `RunAgentInput.tools` is called, **end the run**, let the frontend execute,
   add tool-result messages, and **start another run**.

6. The newer **interrupt** types are HITL-oriented (`reason: "tool_call"` is
   documented as approval-before-execute). We reuse that pause/resume wire
   format for frontend *execution* because it is what the installed packages
   actually ship. We do **not** add CopilotKit or a custom `/api/tool-result`.

### Mechanism used here

```text
Run 1: TOOL_CALL_START / ARGS / END for navigate_to_employee
     → MESSAGES_SNAPSHOT
     → RUN_FINISHED { outcome: interrupt, reason: tool_call, toolCallId }
     → HTTP stream ends (backend is not blocked)

Angular executes Router.navigate

Run 2: same threadId
     → messages include assistant toolCalls + tool result message
     → resume: [{ interruptId, status: "resolved", payload: result }]
     → backend emits TOOL_CALL_RESULT
     → LLM continues
     → RUN_FINISHED { outcome: success }
```

`interruptId` is the original `toolCallId` so resume correlation stays obvious.

## Tool ownership

Ownership is an explicit catalog, not a name heuristic.

```text
ToolExecutionTarget.Server
ToolExecutionTarget.Frontend
```

Merge rules:

- Server tools come from `ToolRegistry` (`get_employee`, `get_leave_balance`).
- Frontend tools come from `RunAgentInput.tools` (`navigate_to_employee`).
- If a client advertises a name the server already owns, the **server wins**.
  A browser cannot hijack `get_employee`.
- Unknown names are **not** treated as frontend tools. They return an error
  JSON to the model. Angular never executes them.

Angular has a second, independent allow-list (`FrontendToolService`). The
browser executes a tool only if:

1. this frontend advertised it (`FRONTEND_TOOLS`)
2. it exists in the executor
3. arguments pass validation

`delete_everything` → `{ success: false, error: "unsupported tool" }` and no
navigation.

## How frontend tools reach the LLM

`AgentRunService` merges:

```text
ToolRegistry definitions
  +
RunAgentInput.tools
```

and passes the combined `LlmToolDefinition` list into `ILlmProvider`. OpenAI
sees three function tools. It still never sees AG-UI types.

## Argument accumulation

AG-UI arguments are streamed:

```text
TOOL_CALL_START
TOOL_CALL_ARGS  "{\"employee"
TOOL_CALL_ARGS  "Id\":"
TOOL_CALL_ARGS  "101}"
TOOL_CALL_END
```

Angular concatenates `delta` values **by `toolCallId`**. It parses JSON only
on `TOOL_CALL_END`. Malformed JSON becomes a failed tool result; the app does
not crash.

## Argument validation

LLM output is untrusted input, including frontend tools.

`employeeId` must be present and a positive integer (or a digits-only string).
`"abc"`, `1.5`, missing fields, and non-objects all fail.

On failure:

- Angular does **not** call `Router.navigate`
- the tool result is `{ success: false, error: "Invalid employee ID" }`
- the agent can continue and explain the error

## How Angular executes the tool

`FrontendToolService.execute(name, args)` is an explicit switch, not dynamic
`eval` or arbitrary routing. The only allowed capability is:

```ts
router.navigate(['/employees', employeeId])
```

Success result:

```json
{ "success": true, "employeeId": 101, "route": "/employees/101" }
```

The profile component only displays the route id. No HR API. Its job is to
prove the frontend tool ran.

## How the result returns / how the agent resumes

There is no `/api/tool-result`. Continuation is another `POST /api/agent/run`
using official fields:

- `threadId` — same conversation
- `parentRunId` — the interrupted run
- `messages` — snapshot plus the new `role: "tool"` message
- `resume[]` — addresses every interrupt
- `tools` — frontend tools advertised again

The backend:

1. maps assistant `toolCalls` and tool messages into `LlmMessage`
2. emits `TOOL_CALL_RESULT` for each resume entry (AG-UI audit trail)
3. does **not** call `ToolRegistry` for `navigate_to_employee`
4. calls the LLM again with the tool result in the conversation

A tool result is submitted once per `toolCallId`. Replays of the same resume
are ignored.

## Server tools still work

`How many leave days does Ahmed have?` remains:

```text
LLM → get_leave_balance → ASP.NET Core → result → LLM → answer
```

Angular shows activity but does not execute it.

## Security

| Environment | Rule |
|---|---|
| Server tools | Executed in ASP.NET Core. Still need validation; not a blank check. |
| Frontend tools | Executed in the browser. Must be allow-listed. |

This phase allows exactly `navigate_to_employee`. There is no
`execute_javascript` and no `navigate_to_arbitrary_url`.

## Cancellation

| Moment | Behavior |
|---|---|
| LLM stream | HTTP abort cancels `CancellationToken`; no further events. |
| Server tool | `ExecuteAsync` sees the same token. |
| Waiting for frontend tool | The first HTTP stream is already finished (`RUN_FINISHED` interrupt). Nothing is blocked server-side. |
| Frontend execution | Sending a new prompt increments a continuation sequence; an in-flight resume is dropped. |
| Continuation run | Same as a normal run: abort cancels that HTTP request. |

This is not a distributed two-phase cancel protocol. It is the smallest
behavior that matches raw HTTP+SSE.

## Multi-tool flow (the important test)

```text
User: Open Ahmed's profile.

LLM: I need Ahmed's id → get_employee({ employeeName: "Ahmed" })
Backend: { id: 101, name: "Ahmed Ali", department: "Engineering" }
LLM: navigate_to_employee({ employeeId: 101 })
Angular: Router → /employees/101
Agent: tool result → LLM → final response
```

Angular does **not** hardcode Ahmed → 101.

## Limitations of this raw AG-UI implementation

- No automatic client (`@ag-ui/client` HttpAgent / CopilotKit) to hide resume.
- The backend is **stateless**. Conversation for resume lives in
  `messages` + `MESSAGES_SNAPSHOT`, not a server checkpoint store.
- Interrupt `reason: "tool_call"` is used for frontend *execution*, not HITL
  approval. Phase 6+ should not confuse those.
- Each continuation is a new run, so `MaxToolIterations` resets per run.
- Shared route context ("how many leave days does **this** employee have?")
  is intentionally **not** implemented.

## Intentionally not implemented

- generative UI
- shared application state / current-route context
- human-in-the-loop confirmation
- write/business tools
- RAG, MCP, LangGraph, CopilotKit
- authentication, database, multi-agent
