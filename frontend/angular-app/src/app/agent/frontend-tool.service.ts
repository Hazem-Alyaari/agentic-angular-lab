import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FRONTEND_TOOL_NAMES, NAVIGATE_TO_EMPLOYEE } from './frontend-tools';

export interface FrontendToolResult {
  success: boolean;
  employeeId?: number;
  route?: string;
  error?: string;
}

/**
 * Allow-listed Angular capabilities the agent may request.
 * Unknown names never execute anything.
 */
@Injectable({
  providedIn: 'root'
})
export class FrontendToolService {
  private readonly router = inject(Router);

  isFrontendTool(name: string): boolean {
    return FRONTEND_TOOL_NAMES.has(name);
  }

  async execute(toolName: string, args: unknown): Promise<FrontendToolResult> {
    if (!this.isFrontendTool(toolName)) {
      return { success: false, error: 'unsupported tool' };
    }

    switch (toolName) {
      case NAVIGATE_TO_EMPLOYEE:
        return this.navigateToEmployee(args);
      default:
        return { success: false, error: 'unsupported tool' };
    }
  }

  private async navigateToEmployee(args: unknown): Promise<FrontendToolResult> {
    const employeeId = parseEmployeeId(args);
    if (employeeId === null) {
      return { success: false, error: 'Invalid employee ID' };
    }

    const route = `/employees/${employeeId}`;
    const navigated = await this.router.navigate(['/employees', employeeId]);
    if (!navigated) {
      return { success: false, error: 'Navigation failed', employeeId, route };
    }

    return { success: true, employeeId, route };
  }
}

function parseEmployeeId(args: unknown): number | null {
  if (args === null || typeof args !== 'object' || Array.isArray(args)) {
    return null;
  }

  if (!Object.prototype.hasOwnProperty.call(args, 'employeeId')) {
    return null;
  }

  const raw = (args as { employeeId: unknown }).employeeId;
  if (typeof raw === 'number') {
    return Number.isInteger(raw) && raw > 0 ? raw : null;
  }

  if (typeof raw === 'string' && /^[0-9]+$/.test(raw)) {
    const value = Number(raw);
    return Number.isSafeInteger(value) && value > 0 ? value : null;
  }

  return null;
}
