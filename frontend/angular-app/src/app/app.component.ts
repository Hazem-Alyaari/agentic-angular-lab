import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterOutlet } from '@angular/router';
import {
  EventType,
  type AssistantMessage,
  type BaseEvent,
  type Message,
  type MessagesSnapshotEvent,
  type ResumeEntry,
  type RunErrorEvent,
  type RunFinishedEvent,
  type TextMessageContentEvent,
  type TextMessageStartEvent,
  type ToolCallArgsEvent,
  type ToolCallEndEvent,
  type ToolCallResultEvent,
  type ToolCallStartEvent,
  type ToolMessage
} from '@ag-ui/core';
import { Subscription } from 'rxjs';
import type { RunAgentInput } from './agent/ag-ui.types';
import { AgUiService } from './agent/ag-ui.service';
import { FrontendToolService } from './agent/frontend-tool.service';
import { ToolCallBuffer } from './agent/tool-call-buffer';

type RunStatus = 'idle' | 'running' | 'error';

interface ToolActivity {
  toolCallId: string;
  name: string;
  target: 'server' | 'browser';
  status: 'calling' | 'done';
}

interface PendingFrontendCall {
  toolCallId: string;
  name: string;
}

@Component({
  selector: 'app-root',
  imports: [FormsModule, RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'AG-UI Streaming Lab';
  healthStatus = 'checking';
  prompt = '';
  status: RunStatus = 'idle';
  assistantText = '';
  errorMessage = '';
  activities: ToolActivity[] = [];
  private currentMessageId: string | null = null;
  private runSubscription: Subscription | null = null;
  private threadId = '';
  private currentRunId = '';
  private messages: Message[] = [];
  private pendingFrontendCalls: PendingFrontendCall[] = [];
  private readonly submittedToolCallIds = new Set<string>();
  private readonly toolArgs = new ToolCallBuffer();
  private readonly frontendResults = new Map<string, ToolMessage>();
  private readonly frontendExecutions = new Map<string, Promise<void>>();
  private continuationSeq = 0;
  private awaitingFrontend = false;
  private currentAssistant: AssistantMessage | null = null;

  private readonly http = inject(HttpClient);
  private readonly agUi = inject(AgUiService);
  private readonly frontendTools = inject(FrontendToolService);

  ngOnInit(): void {
    this.http.get<{ status: string }>('/api/health').subscribe({
      next: (response) => {
        this.healthStatus = response.status;
      },
      error: () => {
        this.healthStatus = 'unavailable';
      }
    });
  }

  ngOnDestroy(): void {
    this.runSubscription?.unsubscribe();
  }

  send(): void {
    const text = this.prompt.trim();
    if (!text || this.status === 'running') {
      return;
    }

    this.continuationSeq += 1;
    this.awaitingFrontend = false;
    this.runSubscription?.unsubscribe();
    this.status = 'running';
    this.errorMessage = '';
    this.assistantText = '';
    this.activities = [];
    this.currentMessageId = null;
    this.pendingFrontendCalls = [];
    this.submittedToolCallIds.clear();
    this.toolArgs.clear();
    this.frontendResults.clear();
    this.frontendExecutions.clear();
    this.currentAssistant = null;

    const input = this.agUi.createUserRun(text);
    this.threadId = input.threadId;
    this.currentRunId = input.runId;
    this.messages = [...input.messages];
    this.subscribeToRun(input);
  }

  private subscribeToRun(input: RunAgentInput): void {
    this.currentRunId = input.runId;
    this.runSubscription = this.agUi.run(input).subscribe({
      next: (event) => this.handleEvent(event),
      error: (error: unknown) => {
        this.awaitingFrontend = false;
        this.status = 'error';
        this.errorMessage =
          error instanceof Error ? error.message : 'Unexpected AG-UI error';
      },
      complete: () => {
        if (this.status === 'running' && !this.awaitingFrontend) {
          this.status = 'idle';
        }
      }
    });
  }

  private handleEvent(event: BaseEvent): void {
    switch (event.type) {
      case EventType.RUN_STARTED:
        this.status = 'running';
        break;

      case EventType.MESSAGES_SNAPSHOT: {
        const snapshot = event as MessagesSnapshotEvent;
        this.messages = snapshot.messages;
        this.currentAssistant = null;
        this.currentMessageId = null;
        break;
      }

      case EventType.TEXT_MESSAGE_START: {
        this.flushAssistantMessage();
        const start = event as TextMessageStartEvent;
        this.currentMessageId = start.messageId;
        this.currentAssistant = {
          id: start.messageId,
          role: 'assistant',
          content: ''
        };
        if (this.assistantText.length > 0) {
          this.assistantText += '\n';
        }
        break;
      }

      case EventType.TEXT_MESSAGE_CONTENT: {
        const content = event as TextMessageContentEvent;
        if (this.currentMessageId && content.messageId !== this.currentMessageId) {
          break;
        }
        this.assistantText += content.delta;
        if (this.currentAssistant) {
          this.currentAssistant = {
            ...this.currentAssistant,
            content: (this.currentAssistant.content ?? '') + content.delta
          };
        }
        break;
      }

      case EventType.TEXT_MESSAGE_END:
        this.flushAssistantMessage();
        break;

      case EventType.TOOL_CALL_START: {
        const start = event as ToolCallStartEvent;
        this.ensureAssistantForToolCall(start.parentMessageId);
        this.appendToolCall(start.toolCallId, start.toolCallName);
        this.activities = [
          ...this.activities,
          {
            toolCallId: start.toolCallId,
            name: start.toolCallName,
            target: this.frontendTools.isFrontendTool(start.toolCallName)
              ? 'browser'
              : 'server',
            status: 'calling'
          }
        ];
        if (this.frontendTools.isFrontendTool(start.toolCallName)) {
          this.pendingFrontendCalls = [
            ...this.pendingFrontendCalls,
            { toolCallId: start.toolCallId, name: start.toolCallName }
          ];
        }
        break;
      }

      case EventType.TOOL_CALL_ARGS: {
        const args = event as ToolCallArgsEvent;
        this.toolArgs.append(args.toolCallId, args.delta);
        this.updateToolCallArguments(args.toolCallId, args.delta);
        break;
      }

      case EventType.TOOL_CALL_END: {
        const end = event as ToolCallEndEvent;
        if (this.pendingFrontendCalls.some((call) => call.toolCallId === end.toolCallId)) {
          void this.executeFrontendToolIfNeeded(end.toolCallId);
        }
        break;
      }

      case EventType.TOOL_CALL_RESULT: {
        const result = event as ToolCallResultEvent;
        this.flushAssistantMessage();
        this.markActivityDone(result.toolCallId);
        this.appendToolMessage({
          id: result.messageId,
          role: 'tool',
          toolCallId: result.toolCallId,
          content: result.content
        });
        this.submittedToolCallIds.add(result.toolCallId);
        break;
      }

      case EventType.RUN_FINISHED: {
        const finished = event as RunFinishedEvent;
        if (finished.outcome?.type === 'interrupt') {
          this.awaitingFrontend = true;
          void this.continueAfterFrontendTools(finished);
        } else {
          this.awaitingFrontend = false;
          this.status = 'idle';
        }
        break;
      }

      case EventType.RUN_ERROR: {
        const runError = event as RunErrorEvent;
        this.awaitingFrontend = false;
        this.status = 'error';
        this.errorMessage = runError.message;
        break;
      }

      default:
        break;
    }
  }

  private executeFrontendToolIfNeeded(toolCallId: string): Promise<void> {
    const existing = this.frontendExecutions.get(toolCallId);
    if (existing) {
      return existing;
    }

    const run = this.runFrontendTool(toolCallId);
    this.frontendExecutions.set(toolCallId, run);
    return run;
  }

  private async runFrontendTool(toolCallId: string): Promise<void> {
    const pending = this.pendingFrontendCalls.find(
      (call) => call.toolCallId === toolCallId
    );
    if (
      !pending ||
      this.submittedToolCallIds.has(toolCallId) ||
      this.frontendResults.has(toolCallId)
    ) {
      return;
    }

    const rawArgs = this.toolArgs.take(toolCallId);
    let parsed: unknown;
    try {
      parsed = JSON.parse(rawArgs.length === 0 ? '{}' : rawArgs);
    } catch {
      parsed = null;
    }

    const result =
      parsed === null
        ? { success: false, error: 'Malformed tool arguments' }
        : await this.frontendTools.execute(pending.name, parsed);

    this.frontendResults.set(toolCallId, this.toToolMessage(toolCallId, result));
    this.markActivityDone(toolCallId);
  }

  private async continueAfterFrontendTools(finished: RunFinishedEvent): Promise<void> {
    const seq = this.continuationSeq;
    const interrupts = finished.outcome?.type === 'interrupt'
      ? finished.outcome.interrupts
      : [];

    const resume: ResumeEntry[] = [];
    for (const interrupt of interrupts) {
      const toolCallId = interrupt.toolCallId ?? interrupt.id;
      if (this.submittedToolCallIds.has(toolCallId)) {
        continue;
      }

      const pending = this.pendingFrontendCalls.find(
        (call) => call.toolCallId === toolCallId
      );
      if (pending && !this.frontendResults.has(toolCallId)) {
        await this.executeFrontendToolIfNeeded(toolCallId);
      }

      const toolMessage =
        this.frontendResults.get(toolCallId) ??
        this.toToolMessage(toolCallId, {
          success: false,
          error: pending ? 'Frontend tool result was missing.' : 'unsupported tool'
        });

      this.appendToolMessage(toolMessage);
      this.submittedToolCallIds.add(toolCallId);
      resume.push({
        interruptId: interrupt.id,
        status: 'resolved',
        payload: this.parsePayload(toolMessage.content)
      });
    }

    if (seq !== this.continuationSeq || resume.length === 0) {
      this.awaitingFrontend = false;
      if (seq === this.continuationSeq && this.status === 'running') {
        this.status = 'idle';
      }
      return;
    }

    this.flushAssistantMessage();
    this.awaitingFrontend = false;
    this.pendingFrontendCalls = [];
    const input = this.agUi.createContinuationRun(
      this.threadId,
      finished.runId || this.currentRunId,
      this.messages,
      resume
    );
    this.subscribeToRun(input);
  }

  private ensureAssistantForToolCall(parentMessageId?: string): void {
    if (this.currentAssistant) {
      return;
    }

    this.currentAssistant = {
      id: parentMessageId || this.agUi.createId('msg'),
      role: 'assistant',
      toolCalls: []
    };
  }

  private appendToolCall(toolCallId: string, name: string): void {
    this.ensureAssistantForToolCall();
    if (!this.currentAssistant) {
      return;
    }

    const toolCalls = this.currentAssistant.toolCalls ?? [];
    if (toolCalls.some((call) => call.id === toolCallId)) {
      return;
    }

    this.currentAssistant = {
      ...this.currentAssistant,
      toolCalls: [
        ...toolCalls,
        {
          id: toolCallId,
          type: 'function',
          function: {
            name,
            arguments: ''
          }
        }
      ]
    };
  }

  private updateToolCallArguments(toolCallId: string, delta: string): void {
    if (!this.currentAssistant?.toolCalls) {
      return;
    }

    this.currentAssistant = {
      ...this.currentAssistant,
      toolCalls: this.currentAssistant.toolCalls.map((call) =>
        call.id === toolCallId
          ? {
              ...call,
              function: {
                ...call.function,
                arguments: call.function.arguments + delta
              }
            }
          : call
      )
    };
  }

  private flushAssistantMessage(): void {
    if (!this.currentAssistant) {
      return;
    }

    const existing = this.messages.find(
      (message) => message.id === this.currentAssistant?.id
    );
    if (existing) {
      this.messages = this.messages.map((message) =>
        message.id === this.currentAssistant?.id ? this.currentAssistant : message
      );
    } else {
      this.messages = [...this.messages, this.currentAssistant];
    }

    this.currentAssistant = null;
    this.currentMessageId = null;
  }

  private appendToolMessage(message: ToolMessage): void {
    if (this.messages.some((item) => item.role === 'tool' && item.toolCallId === message.toolCallId)) {
      return;
    }

    this.messages = [...this.messages, message];
  }

  private markActivityDone(toolCallId: string): void {
    this.activities = this.activities.map((activity) =>
      activity.toolCallId === toolCallId ? { ...activity, status: 'done' } : activity
    );
  }

  private toToolMessage(
    toolCallId: string,
    result: { success: boolean; error?: string; employeeId?: number; route?: string }
  ): ToolMessage {
    const content = JSON.stringify(result);
    return {
      id: this.agUi.createId('tool'),
      role: 'tool',
      toolCallId,
      content,
      error: result.success ? undefined : result.error
    };
  }

  private parsePayload(content: string): unknown {
    try {
      return JSON.parse(content) as unknown;
    } catch {
      return { success: false, error: 'Malformed tool arguments' };
    }
  }
}
