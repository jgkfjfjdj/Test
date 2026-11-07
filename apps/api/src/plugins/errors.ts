import { ErrorCodes, PROBLEM_CONTENT_TYPE } from '@kindling/contracts';
import type { FastifyError, FastifyInstance, FastifyReply, FastifyRequest } from 'fastify';
import { ProblemError } from '../lib/problem.js';

const statusCodeMap: Record<number, { code: string; title: string }> = {
  400: { code: 'common.bad_request', title: 'Bad Request' },
  401: { code: ErrorCodes.unauthorized, title: 'Unauthorized' },
  403: { code: 'common.forbidden', title: 'Forbidden' },
  404: { code: ErrorCodes.notFound, title: 'Not Found' },
  405: { code: 'common.method_not_allowed', title: 'Method Not Allowed' },
  413: { code: ErrorCodes.payloadTooLarge, title: 'Payload Too Large' },
  415: { code: ErrorCodes.unsupportedMediaType, title: 'Unsupported Media Type' },
  429: { code: ErrorCodes.rateLimited, title: 'Too Many Requests' },
};

function send(reply: FastifyReply, request: FastifyRequest, problem: ProblemError): FastifyReply {
  return reply
    .status(problem.status)
    .type(PROBLEM_CONTENT_TYPE)
    .send(problem.toProblem(request.id, request.url.split('?')[0]));
}

/** Converts every error and 404 into RFC 9457 problem+json. Internal details are never leaked. */
export function registerErrorHandling(app: FastifyInstance): void {
  app.setErrorHandler((error: FastifyError | Error, request, reply) => {
    if (error instanceof ProblemError) {
      if (error.status >= 500) request.log.error({ err: error }, 'request failed');
      return send(reply, request, error);
    }

    const fastifyError = error as FastifyError;
    if (fastifyError.validation) {
      const context = fastifyError.validationContext ?? 'request';
      const fieldErrors = fastifyError.validation.map((v) => ({
        path: `${context}${v.instancePath ?? ''}`,
        message: v.message ?? 'is invalid',
      }));
      return send(
        reply,
        request,
        new ProblemError(400, ErrorCodes.validationFailed, 'Validation Failed', undefined, fieldErrors),
      );
    }

    const status = fastifyError.statusCode;
    if (status !== undefined && status >= 400 && status < 500) {
      const mapped = statusCodeMap[status] ?? { code: `common.http_${status}`, title: 'Client Error' };
      return send(reply, request, new ProblemError(status, mapped.code, mapped.title));
    }

    request.log.error({ err: error }, 'unhandled error');
    return send(reply, request, ProblemError.internal());
  });

  app.setNotFoundHandler((request, reply) =>
    send(
      reply,
      request,
      ProblemError.notFound(`No route for ${request.method} ${request.url.split('?')[0]}`),
    ),
  );
}
