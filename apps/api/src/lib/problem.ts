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
  ) {
    super(`${code}: ${detail ?? title}`);
    this.name = 'ProblemError';
  }

  toProblem(requestId?: string, instance?: string): ProblemDetails {
    return {
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

  static notFound(detail?: string): ProblemError {
    return new ProblemError(404, ErrorCodes.notFound, 'Not Found', detail);
  }

  static internal(): ProblemError {
    return new ProblemError(500, ErrorCodes.internal, 'Internal Server Error');
  }
}
