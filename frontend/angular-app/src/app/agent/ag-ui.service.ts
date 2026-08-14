import { Injectable } from '@angular/core';
import { Observable, Subscriber } from 'rxjs';
import {
  EventSchemas,
  type BaseEvent,
  type RunAgentInput
} from './ag-ui.types';

/**
 * Minimal AG-UI HTTP+SSE client for protocol learning.
 *
 * Why not @ag-ui/client HttpAgent?
 * HttpAgent also manages message/state history and subscriber middleware.
 * We keep the protocol path visible:
 * POST RunAgentInput → SSE frames → EventSchemas.parse → Observable event.
 *
 * This service is provider-agnostic: it never sees OpenAI/Claude/etc.
 */
@Injectable({
  providedIn: 'root'
})
export class AgUiService {
  private readonly endpoint = '/api/agent/run';

  run(userText: string, threadId = this.createId('thread')): Observable<BaseEvent> {
    const input: RunAgentInput = {
      threadId,
      runId: this.createId('run'),
      state: {},
      messages: [
        {
          id: this.createId('msg'),
          role: 'user',
          content: userText
        }
      ],
      tools: [],
      context: [],
      forwardedProps: {}
    };

    return new Observable<BaseEvent>((subscriber) => {
      const controller = new AbortController();

      void this.consumeSse(input, subscriber, controller.signal);

      return () => controller.abort();
    });
  }

  private async consumeSse(
    input: RunAgentInput,
    subscriber: Subscriber<BaseEvent>,
    signal: AbortSignal
  ): Promise<void> {
    try {
      const response = await fetch(this.endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream'
        },
        body: JSON.stringify(input),
        signal
      });

      if (!response.ok) {
        subscriber.error(new Error(`AG-UI run failed with HTTP ${response.status}`));
        return;
      }

      if (!response.body) {
        subscriber.error(new Error('AG-UI run returned an empty body'));
        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) {
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        buffer = this.flushSseFrames(buffer, subscriber);
      }

      buffer += decoder.decode();
      this.flushSseFrames(buffer, subscriber, true);
      subscriber.complete();
    } catch (error) {
      if (signal.aborted) {
        subscriber.complete();
        return;
      }

      const message =
        error instanceof Error ? error.message : 'AG-UI run request failed';
      subscriber.error(new Error(message));
    }
  }

  private flushSseFrames(
    buffer: string,
    subscriber: Subscriber<BaseEvent>,
    flushRemainder = false
  ): string {
    const frames = buffer.split('\n\n');
    const remainder = flushRemainder ? '' : (frames.pop() ?? '');

    for (const frame of frames) {
      const dataLines = frame
        .split('\n')
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).trimStart());

      if (dataLines.length === 0) {
        continue;
      }

      const payload = dataLines.join('\n');
      if (!payload || payload === '[DONE]') {
        continue;
      }

      try {
        const parsed: unknown = JSON.parse(payload);
        const event = EventSchemas.parse(parsed);
        subscriber.next(event);
      } catch (error) {
        const detail =
          error instanceof Error ? error.message : 'unknown parse error';
        subscriber.error(new Error(`Malformed or unexpected AG-UI event: ${detail}`));
        return '';
      }
    }

    return remainder;
  }

  private createId(prefix: string): string {
    return `${prefix}_${crypto.randomUUID().replaceAll('-', '')}`;
  }
}
