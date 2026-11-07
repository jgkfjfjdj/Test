import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';

/** RFC 9457 problem body returned by every failing API call. */
export interface Problem {
  type?: string;
  title: string;
  status: number;
  code: string;
  detail?: string;
  requestId?: string;
  errors?: { path: string; message: string }[];
  [extension: string]: unknown;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly problem: Problem,
  ) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiError';
  }
}

/** Short user-facing message for any error (API problem, network failure, bug). */
export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const fieldErrors = err.problem.errors
      ?.map((e) => `${e.path.replace(/^body\//, '')}: ${e.message}`)
      .join('; ');
    return fieldErrors ? `${err.message} (${fieldErrors})` : err.message;
  }
  if (err instanceof TypeError) return 'Network error — check your connection and try again.';
  return err instanceof Error ? err.message : 'Something went wrong.';
}

// ---------------------------------------------------------------- CSRF

let csrfToken: string | null = null;

/** The CSRF token comes from login / auth/me and is kept in memory only (never in storage). */
export function setCsrfToken(token: string | null): void {
  csrfToken = token;
}

const csrf: Middleware = {
  onRequest({ request }) {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method) && csrfToken) {
      request.headers.set('x-csrf-token', csrfToken);
    }
    return request;
  },
};

// fetch is looked up per call (not captured at import) so tests can stub it.
export const client = createClient<paths>({
  // Absolute same-origin URLs (some Request implementations reject relative ones).
  baseUrl: typeof window === 'undefined' ? '' : window.location.origin,
  credentials: 'same-origin',
  fetch: (request) => globalThis.fetch(request),
});
client.use(csrf);

// ---------------------------------------------------------------- session-level handlers

type StepUpHandler = () => Promise<boolean>;
let stepUpHandler: StepUpHandler | null = null;
let unauthorizedHandler: (() => void) | null = null;

/** Registered by the step-up dialog: resolves true once the user confirmed a fresh code. */
export function setStepUpHandler(handler: StepUpHandler | null): void {
  stepUpHandler = handler;
}

/** Registered by the session provider: called when the server says the session has ended. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

interface FetchResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

async function unwrap<T>(result: Promise<FetchResult<T>>): Promise<T> {
  const { data, error, response } = await result;
  if (response.ok) return data as T;
  const body = (error ?? {}) as Partial<Problem>;
  const problem: Problem = {
    title: body.title ?? response.statusText ?? 'Error',
    status: body.status ?? response.status,
    code: body.code ?? `http.${response.status}`,
    ...body,
  };
  throw new ApiError(response.status, problem.code, problem);
}

/**
 * Runs an API request and returns its data or throws ApiError. If the server asks for a fresh
 * authenticator confirmation (step-up), the dialog is shown and the request retried once.
 */
export async function call<T>(request: () => Promise<FetchResult<T>>): Promise<T> {
  try {
    return await unwrap(request());
  } catch (err) {
    if (err instanceof ApiError) {
      if (err.code === 'staff.step_up_required' && stepUpHandler) {
        if (await stepUpHandler()) return unwrap(request());
      } else if (err.status === 401 && err.code === 'staff.unauthorized') {
        unauthorizedHandler?.();
      }
    }
    throw err;
  }
}
