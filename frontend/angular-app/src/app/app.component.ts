import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  EventType,
  type BaseEvent,
  type RunErrorEvent,
  type TextMessageContentEvent,
  type TextMessageStartEvent,
  type ToolCallResultEvent,
  type ToolCallStartEvent
} from '@ag-ui/core';
import { Subscription } from 'rxjs';
import { AgUiService } from './agent/ag-ui.service';

type RunStatus = 'idle' | 'running' | 'error';

interface ToolActivity {
  toolCallId: string;
  name: string;
  status: 'calling' | 'done';
}

@Component({
  selector: 'app-root',
  imports: [FormsModule],
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

  private readonly http = inject(HttpClient);
  private readonly agUi = inject(AgUiService);

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

    this.runSubscription?.unsubscribe();
    this.status = 'running';
    this.errorMessage = '';
    this.assistantText = '';
    this.activities = [];
    this.currentMessageId = null;

    this.runSubscription = this.agUi.run(text).subscribe({
      next: (event) => this.handleEvent(event),
      error: (error: unknown) => {
        this.status = 'error';
        this.errorMessage =
          error instanceof Error ? error.message : 'Unexpected AG-UI error';
      },
      complete: () => {
        if (this.status === 'running') {
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

      case EventType.TEXT_MESSAGE_START: {
        const start = event as TextMessageStartEvent;
        this.currentMessageId = start.messageId;
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
        break;
      }

      case EventType.TEXT_MESSAGE_END:
        break;

      case EventType.TOOL_CALL_START: {
        const start = event as ToolCallStartEvent;
        this.activities = [
          ...this.activities,
          {
            toolCallId: start.toolCallId,
            name: start.toolCallName,
            status: 'calling'
          }
        ];
        break;
      }

      case EventType.TOOL_CALL_ARGS:
      case EventType.TOOL_CALL_END:
        break;

      case EventType.TOOL_CALL_RESULT: {
        const result = event as ToolCallResultEvent;
        this.activities = this.activities.map((activity) =>
          activity.toolCallId === result.toolCallId
            ? { ...activity, status: 'done' }
            : activity
        );
        break;
      }

      case EventType.RUN_FINISHED:
        this.status = 'idle';
        break;

      case EventType.RUN_ERROR: {
        const runError = event as RunErrorEvent;
        this.status = 'error';
        this.errorMessage = runError.message;
        break;
      }

      default:
        break;
    }
  }
}
