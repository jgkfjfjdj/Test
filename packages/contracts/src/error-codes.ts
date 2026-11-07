/**
 * Central catalogue of API error codes. Codes are stable once released:
 * never rename or reuse one — add a new code instead.
 */
export const ErrorCodes = {
  internal: 'common.internal_error',
  notFound: 'common.not_found',
  validationFailed: 'common.validation_failed',
  rateLimited: 'common.rate_limited',
  unavailable: 'common.service_unavailable',
  payloadTooLarge: 'common.payload_too_large',
  unsupportedMediaType: 'common.unsupported_media_type',
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];
