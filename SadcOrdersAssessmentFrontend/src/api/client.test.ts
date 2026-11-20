import { z } from 'zod';
import { ApiError, apiRequest, setTokenProvider, toQueryString } from './client';

function mockFetch(status: number, body: unknown, headers: Record<string, string> = {}) {
  const fn = vi.fn().mockResolvedValue(
    new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
    }),
  );
  vi.stubGlobal('fetch', fn);
  return fn;
}

describe('apiRequest', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setTokenProvider(async () => null);
  });

  it('sends the bearer token and a correlation id, and validates the response', async () => {
    setTokenProvider(async () => 'token-123');
    const fetchMock = mockFetch(200, { value: 42 });

    const { data } = await apiRequest('/api/thing', { schema: z.object({ value: z.number() }) });

    expect(data.value).toBe(42);
    const init = fetchMock.mock.calls[0]![1] as RequestInit;
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token-123');
    expect(headers['X-Correlation-ID']).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('turns Problem Details into an ApiError with field errors', async () => {
    mockFetch(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      code: 'validation_failed',
      errors: { currencyCode: ['Currency ZAR is not permitted for customers in BW. Allowed: BWP.'] },
      traceId: 'trace-1',
    });

    const error = await apiRequest('/api/orders', { method: 'POST', body: {}, schema: z.unknown() }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.fieldErrors.currencyCode?.[0]).toContain('BWP');
    expect(apiError.problem.traceId).toBe('trace-1');
    expect(apiError.isTransient).toBe(false);
  });

  it('rejects responses that break the contract', async () => {
    mockFetch(200, { value: 'not a number' });
    vi.spyOn(console, 'error').mockImplementation(() => {});

    await expect(apiRequest('/api/thing', { schema: z.object({ value: z.number() }) })).rejects.toMatchObject({
      problem: { title: 'Unexpected response' },
    });
  });

  it('reports network failures as transient', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = (await apiRequest('/api/thing', { schema: z.unknown() }).catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(0);
    expect(error.isTransient).toBe(true);
  });
});

describe('toQueryString', () => {
  it('skips empty values', () => {
    expect(toQueryString({ search: '', page: 1, status: undefined, sort: '-createdAt' })).toBe('?page=1&sort=-createdAt');
  });
});
