export class ToolCallBuffer {
  private readonly chunks = new Map<string, string>();

  append(toolCallId: string, delta: string): void {
    this.chunks.set(toolCallId, (this.chunks.get(toolCallId) ?? '') + delta);
  }

  take(toolCallId: string): string {
    const value = this.chunks.get(toolCallId) ?? '';
    this.chunks.delete(toolCallId);
    return value;
  }

  clear(): void {
    this.chunks.clear();
  }
}
