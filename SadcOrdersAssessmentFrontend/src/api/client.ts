import type { z } from 'zod';
import { ProblemDetailsSchema, type ProblemDetails } from './schemas';

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

/** An API failure carrying the server's Problem Details, with field errors keyed by camelCase path. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed (${status})`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }

  /** True for failures where retrying the same request might succeed (network, 5xx, 429). */
  get isTransient(): boolean {
    return this.status === 0 || this.status === 429 || this.status >= 500;
  }
}

type TokenProvider = () => Promise<string | null>;
let tokenProvider: TokenProvider = async () => null;

/** The auth layer registers how to get a bearer token (mock dev token here; MSAL with real Entra). */
export function setTokenProvider(provider: TokenProvider): void {
  tokenProvider = provider;
}

export interface RequestOptions<T> {
  method?: 'GET' | 'POST' | 'PUT';
  body?: unknown;
  headers?: Record<string, string>;
  schema: z.ZodType<T>;
  signal?: AbortSignal;
  anonymous?: boolean;
}

export interface ApiResponse<T> {
  data: T;
  headers: Headers;
}

export async function apiRequest<T>(path: string, options: RequestOptions<T>): Promise<ApiResponse<T>> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Correlation-ID': crypto.randomUUID(),
    ...options.headers,
  };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (!options.anonymous) {
    const token = await tokenProvider();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  let response: Response;
  try {
    response = await fetch(`${BASE_URL}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new ApiError(0, { title: 'Network error', detail: 'Could not reach the server. Check your connection and try again.' });
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response));
  }

  const json: unknown = await response.json();
  const parsed = options.schema.safeParse(json);
  if (!parsed.success) {
    // The contract drifted: say so clearly instead of rendering half-broken data.
    console.error('API contract mismatch', path, parsed.error.issues);
    throw new ApiError(response.status, {
      title: 'Unexpected response',
      detail: 'The server response did not match the expected format.',
    });
  }
  return { data: parsed.data, headers: response.headers };
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    const parsed = ProblemDetailsSchema.safeParse(await response.json());
    if (parsed.success) {
      return parsed.data;
    }
  } catch {
    // Empty or non-JSON body.
  }
  return { status: response.status, title: response.statusText || `HTTP ${response.status}` };
}

export function toQueryString(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }
  const qs = search.toString();
  return qs ? `?${qs}` : '';
}
