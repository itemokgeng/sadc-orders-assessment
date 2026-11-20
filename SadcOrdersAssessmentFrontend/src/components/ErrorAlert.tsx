import { ApiError } from '../api/client';

/** Shows an API/Problem Details error. Field errors are shown next to their inputs, not here. */
export function ErrorAlert({ error, title }: { error: unknown; title?: string }) {
  if (!error) {
    return null;
  }
  const apiError = error instanceof ApiError ? error : null;
  const message = apiError?.message ?? (error instanceof Error ? error.message : 'Something went wrong.');
  const reference = apiError?.problem.traceId;

  return (
    <div role="alert" className="alert">
      <strong>{title ?? apiError?.problem.title ?? 'Error'}</strong>
      <p>{message}</p>
      {reference && <p className="muted">Reference: {reference}</p>}
    </div>
  );
}
