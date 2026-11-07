import { ErrorCodes, STEP_UP_PERMISSIONS, type Permission } from '@kindling/contracts';
import type { FastifyReply, FastifyRequest, preHandlerAsyncHookHandler } from 'fastify';
import { ProblemError } from '../lib/problem.js';
import {
  STAFF_COOKIE,
  STEP_UP_WINDOW_MS,
  StaffService,
  type StaffIdentity,
} from '../modules/staff/service.js';

declare module 'fastify' {
  interface FastifyRequest {
    staff?: StaffIdentity;
    staffSessionToken?: string;
  }
}

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

export interface StaffGuardOptions {
  /**
   * Force (true) or skip (false) the TOTP step-up check. By default it applies to state-changing
   * requests guarded by a permission on the STEP_UP_PERMISSIONS list; reads never need it.
   */
  stepUp?: boolean;
}

/**
 * preHandler for staff routes: valid session cookie, CSRF header on state-changing requests,
 * the permission (if given), and a recent step-up for dangerous permissions.
 */
export function requireStaff(
  permission?: Permission,
  options: StaffGuardOptions = {},
): preHandlerAsyncHookHandler {
  return async function staffGuard(request: FastifyRequest, _reply: FastifyReply) {
    const token = request.cookies[STAFF_COOKIE];
    if (!token)
      throw ProblemError.unauthorized(ErrorCodes.staffUnauthorized, 'Sign in to the staff console.');
    const service = new StaffService(request.server.ctx);
    const staff = await service.authenticate(token);
    if (!staff)
      throw ProblemError.unauthorized(ErrorCodes.staffUnauthorized, 'Your session has ended. Sign in again.');
    if (
      !SAFE_METHODS.has(request.method) &&
      !service.verifyCsrf(token, request.headers['x-csrf-token'] as string | undefined)
    ) {
      throw ProblemError.forbidden(ErrorCodes.staffCsrfFailed, 'Missing or invalid CSRF token.');
    }
    if (permission && !staff.permissions.has(permission)) {
      throw ProblemError.forbidden(
        ErrorCodes.staffForbidden,
        `This action needs the "${permission}" permission.`,
      );
    }
    const needsStepUp =
      options.stepUp ??
      (!SAFE_METHODS.has(request.method) &&
        permission !== undefined &&
        STEP_UP_PERMISSIONS.includes(permission));
    if (needsStepUp) {
      const fresh =
        staff.stepUpAt && request.server.ctx.now().getTime() - staff.stepUpAt.getTime() <= STEP_UP_WINDOW_MS;
      if (!fresh) {
        throw ProblemError.forbidden(
          ErrorCodes.staffStepUpRequired,
          'Confirm with your authenticator code to continue.',
        );
      }
    }
    request.staff = staff;
    request.staffSessionToken = token;
  };
}

export function staffOf(request: FastifyRequest): StaffIdentity {
  if (!request.staff) throw ProblemError.unauthorized(ErrorCodes.staffUnauthorized);
  return request.staff;
}
