import { ErrorCodes, type ProblemDetails } from '@kindling/contracts';

/**
 * Throw from any route/service to produce an RFC 9457 response.
 * Only `detail` strings that are safe to show to clients belong here.
 */
export class ProblemError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    public readonly title: string,
    public readonly detail?: string,
    public readonly fieldErrors?: { path: string; message: string }[],
    /** Extra members merged into the problem body (RFC 9457 extension members). */
    public readonly extensions?: Record<string, unknown>,
  ) {
    super(`${code}: ${detail ?? title}`);
    this.name = 'ProblemError';
  }

  toProblem(requestId?: string, instance?: string): ProblemDetails & Record<string, unknown> {
    return {
      ...(this.extensions ?? {}),
      type: `https://docs.kindling.invalid/errors/${this.code}`,
      title: this.title,
      status: this.status,
      code: this.code,
      ...(this.detail !== undefined ? { detail: this.detail } : {}),
      ...(instance !== undefined ? { instance } : {}),
      ...(requestId !== undefined ? { requestId } : {}),
      ...(this.fieldErrors !== undefined ? { errors: this.fieldErrors } : {}),
    };
  }

  static badRequest(code: string, detail?: string, extensions?: Record<string, unknown>): ProblemError {
    return new ProblemError(400, code, 'Bad Request', detail, undefined, extensions);
  }

  static unauthorized(code: string = ErrorCodes.unauthorized, detail?: string): ProblemError {
    return new ProblemError(401, code, 'Unauthorized', detail);
  }

  static forbidden(code: string, detail?: string): ProblemError {
    return new ProblemError(403, code, 'Forbidden', detail);
  }

  static notFound(detail?: string, code: string = ErrorCodes.notFound): ProblemError {
    return new ProblemError(404, code, 'Not Found', detail);
  }

  static conflict(code: string, detail?: string, extensions?: Record<string, unknown>): ProblemError {
    return new ProblemError(409, code, 'Conflict', detail, undefined, extensions);
  }

  static unprocessable(code: string, detail?: string): ProblemError {
    return new ProblemError(422, code, 'Unprocessable Content', detail);
  }

  static tooMany(code: string = ErrorCodes.rateLimited, detail?: string): ProblemError {
    return new ProblemError(429, code, 'Too Many Requests', detail);
  }

  static unavailable(code: string, detail?: string, extensions?: Record<string, unknown>): ProblemError {
    return new ProblemError(503, code, 'Service Unavailable', detail, undefined, extensions);
  }

  static internal(): ProblemError {
    return new ProblemError(500, ErrorCodes.internal, 'Internal Server Error');
  }
}

/** Extracts PostgreSQL error code / constraint from driver errors (possibly wrapped by Drizzle). */
export function pgError(err: unknown): { code?: string; constraint?: string } {
  let current: unknown = err;
  for (let depth = 0; depth < 4 && current && typeof current === 'object'; depth++) {
    const e = current as { code?: unknown; constraint_name?: unknown; cause?: unknown };
    if (typeof e.code === 'string' && /^[0-9A-Z]{5}$/.test(e.code)) {
      return {
        code: e.code,
        ...(typeof e.constraint_name === 'string' ? { constraint: e.constraint_name } : {}),
      };
    }
    current = e.cause;
  }
  return {};
}
