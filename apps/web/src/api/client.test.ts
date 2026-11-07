import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  call,
  client,
  errorMessage,
  setCsrfToken,
  setStepUpHandler,
  setUnauthorizedHandler,
} from './client';

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });
}

const problem = (status: number, code: string, detail?: string) => ({
  type: 'x',
  title: 'Error',
  status,
  code,
  ...(detail ? { detail } : {}),
});

afterEach(() => {
  vi.unstubAllGlobals();
  setCsrfToken(null);
  setStepUpHandler(null);
  setUnauthorizedHandler(null);
});

describe('API client', () => {
  it('sends the CSRF token on writes only', async () => {
    const seen: { method: string; csrf: string | null }[] = [];
    vi.stubGlobal('fetch', async (request: Request) => {
      seen.push({ method: request.method, csrf: request.headers.get('x-csrf-token') });
      return json(200, { items: [] });
    });
    setCsrfToken('csrf-123');
    await call(() => client.GET('/admin/v1/staff'));
    await call(() => client.POST('/admin/v1/auth/logout'));
    expect(seen).toEqual([
      { method: 'GET', csrf: null },
      { method: 'POST', csrf: 'csrf-123' },
    ]);
  });

  it('turns problem+json responses into ApiError with the stable code', async () => {
    vi.stubGlobal('fetch', async () => json(409, problem(409, 'server.full', 'This server is full.')));
    const err = await call(() => client.GET('/admin/v1/servers')).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err).toMatchObject({ status: 409, code: 'server.full', message: 'This server is full.' });
  });

  it('formats validation errors and network failures for people', () => {
    const validation = new ApiError(400, 'common.validation_failed', {
      title: 'Validation Failed',
      status: 400,
      code: 'common.validation_failed',
      errors: [{ path: 'body/reason', message: 'must not have fewer than 3 characters' }],
    });
    expect(errorMessage(validation)).toBe(
      'Validation Failed (reason: must not have fewer than 3 characters)',
    );
    expect(errorMessage(new TypeError('Failed to fetch'))).toMatch(/Network error/);
  });

  it('asks for step-up once and retries the request', async () => {
    let attempts = 0;
    vi.stubGlobal('fetch', async () => {
      attempts += 1;
      return attempts === 1
        ? json(403, problem(403, 'staff.step_up_required'))
        : json(200, { token: 'kst_new' });
    });
    const handler = vi.fn(async () => true);
    setStepUpHandler(handler);
    const result = await call(() =>
      client.POST('/admin/v1/servers/{id}/token/rotate', {
        params: { path: { id: 'x' } },
        body: { reason: 'leak' },
      }),
    );
    expect(result).toEqual({ token: 'kst_new' });
    expect(handler).toHaveBeenCalledTimes(1);
    expect(attempts).toBe(2);
  });

  it('gives up when step-up is cancelled', async () => {
    vi.stubGlobal('fetch', async () =>
      json(403, problem(403, 'staff.step_up_required', 'Confirm with your authenticator code to continue.')),
    );
    setStepUpHandler(async () => false);
    await expect(call(() => client.POST('/admin/v1/auth/logout'))).rejects.toMatchObject({
      code: 'staff.step_up_required',
    });
  });

  it('reports ended sessions to the session layer', async () => {
    vi.stubGlobal('fetch', async () => json(401, problem(401, 'staff.unauthorized')));
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    await expect(call(() => client.GET('/admin/v1/staff'))).rejects.toBeInstanceOf(ApiError);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
