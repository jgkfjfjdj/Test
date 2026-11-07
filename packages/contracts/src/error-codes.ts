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
  conflict: 'common.conflict',

  // Player authentication
  unauthorized: 'auth.unauthorized',
  invalidCredentials: 'auth.invalid_credentials',
  tooManyAttempts: 'auth.too_many_attempts',
  emailInvalid: 'auth.email_invalid',
  emailTaken: 'auth.email_taken',
  passwordWeak: 'auth.password_weak',
  passwordBreached: 'auth.password_breached',
  invalidRefreshToken: 'auth.invalid_refresh_token',
  refreshTokenReused: 'auth.refresh_token_reused',
  emailTokenInvalid: 'auth.token_invalid',
  appleTokenInvalid: 'auth.apple_token_invalid',
  accountSuspended: 'auth.account_suspended',

  // Accounts
  displayNameInvalid: 'account.display_name_invalid',
  displayNameTaken: 'account.display_name_taken',
  displayNameRequired: 'account.display_name_required',
  deletionNotPending: 'account.deletion_not_pending',
  confirmationRequired: 'account.confirmation_required',

  // Cloud saves
  cloudSaveNotFound: 'cloud_save.not_found',
  cloudSaveConflict: 'cloud_save.version_conflict',
  cloudSaveTooLarge: 'cloud_save.too_large',

  // Maintenance / versions
  maintenance: 'platform.maintenance',
  updateRequired: 'platform.update_required',

  // Staff console
  staffUnauthorized: 'staff.unauthorized',
  staffForbidden: 'staff.forbidden',
  staffStepUpRequired: 'staff.step_up_required',
  staffCsrfFailed: 'staff.csrf_failed',
  staffInvalidCredentials: 'staff.invalid_credentials',
  staffSetupInvalid: 'staff.setup_invalid',
  staffEmailTaken: 'staff.email_taken',
  staffCannotModifySelf: 'staff.cannot_modify_self',
  staffUnknownRole: 'staff.unknown_role',

  // Servers and joining
  serverNotFound: 'server.not_found',
  serverOffline: 'server.offline',
  serverFull: 'server.full',
  serverPasswordRequired: 'server.password_required',
  serverPasswordWrong: 'server.password_wrong',
  serverVersionMismatch: 'server.version_mismatch',
  serverCommandFailed: 'server.command_failed',
  serverCommandTimeout: 'server.command_timeout',
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];
