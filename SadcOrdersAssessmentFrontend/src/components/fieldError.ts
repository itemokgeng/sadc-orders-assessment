import { ApiError } from '../api/client';

/** First server-side error for a field (keys are camelCase paths such as "lineItems[0].quantity"). */
export function fieldError(error: unknown, field: string): string | undefined {
  return error instanceof ApiError ? error.fieldErrors[field]?.[0] : undefined;
}
