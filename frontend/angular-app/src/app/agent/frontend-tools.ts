import type { Tool } from './ag-ui.types';

export const NAVIGATE_TO_EMPLOYEE = 'navigate_to_employee';

/**
 * Client-advertised tools for RunAgentInput.tools.
 * Server tools stay in the ASP.NET Core ToolRegistry and are not listed here.
 */
export const FRONTEND_TOOLS: Tool[] = [
  {
    name: NAVIGATE_TO_EMPLOYEE,
    description:
      'Navigate the user to an employee profile in the Angular application.',
    parameters: {
      type: 'object',
      properties: {
        employeeId: {
          type: 'integer',
          description: 'The employee ID to open.'
        }
      },
      required: ['employeeId'],
      additionalProperties: false
    }
  }
];

export const FRONTEND_TOOL_NAMES = new Set(
  FRONTEND_TOOLS.map((tool) => tool.name)
);
