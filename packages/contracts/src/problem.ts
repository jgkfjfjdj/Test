import { Type, type Static } from 'typebox';

/**
 * RFC 9457 problem details. Every non-2xx API response uses this shape.
 * `code` is a stable, machine-readable identifier that clients map to localized messages.
 */
export const ProblemDetails = Type.Object(
  {
    type: Type.String({ description: 'URI reference identifying the problem type.' }),
    title: Type.String(),
    status: Type.Integer({ minimum: 400, maximum: 599 }),
    code: Type.String({ description: 'Stable error code, e.g. "auth.invalid_credentials".' }),
    detail: Type.Optional(Type.String()),
    instance: Type.Optional(Type.String()),
    requestId: Type.Optional(Type.String()),
    errors: Type.Optional(
      Type.Array(
        Type.Object({
          path: Type.String(),
          message: Type.String(),
        }),
      ),
    ),
  },
  { $id: 'ProblemDetails', additionalProperties: false },
);
export type ProblemDetails = Static<typeof ProblemDetails>;

export const PROBLEM_CONTENT_TYPE = 'application/problem+json';
