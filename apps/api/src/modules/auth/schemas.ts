import { Type } from 'typebox';

export const AccountSummarySchema = Type.Object({
  id: Type.String({ format: 'uuid' }),
  displayName: Type.String(),
  email: Type.Union([Type.String(), Type.Null()]),
  emailVerified: Type.Boolean(),
  status: Type.Union([
    Type.Literal('active'),
    Type.Literal('suspended'),
    Type.Literal('pending_deletion'),
    Type.Literal('deleted'),
  ]),
});

export const SessionResponse = Type.Object({
  accessToken: Type.String(),
  accessTokenExpiresAt: Type.String({ format: 'date-time' }),
  refreshToken: Type.String({ description: 'Rotating; store securely (iOS Keychain). Single use.' }),
  refreshTokenExpiresAt: Type.String({ format: 'date-time' }),
  account: AccountSummarySchema,
  created: Type.Boolean(),
});

export const RefreshResponse = Type.Object({
  accessToken: Type.String(),
  accessTokenExpiresAt: Type.String({ format: 'date-time' }),
  refreshToken: Type.String(),
  refreshTokenExpiresAt: Type.String({ format: 'date-time' }),
});

export const RegisterBody = Type.Object(
  {
    email: Type.String({ maxLength: 254 }),
    password: Type.String({ minLength: 1, maxLength: 1024 }),
    displayName: Type.String({ minLength: 1, maxLength: 32 }),
  },
  { additionalProperties: false },
);

export const LoginBody = Type.Object(
  {
    email: Type.String({ maxLength: 254 }),
    password: Type.String({ minLength: 1, maxLength: 1024 }),
  },
  { additionalProperties: false },
);

export const AppleBody = Type.Object(
  {
    identityToken: Type.String({ minLength: 1, maxLength: 8192 }),
    nonce: Type.String({
      minLength: 16,
      maxLength: 128,
      description: 'Raw nonce; the app sends sha256(nonce) to Apple.',
    }),
    displayName: Type.Optional(Type.String({ minLength: 1, maxLength: 32 })),
  },
  { additionalProperties: false },
);

export const RefreshBody = Type.Object(
  { refreshToken: Type.String({ minLength: 20, maxLength: 128 }) },
  { additionalProperties: false },
);

export const TokenBody = Type.Object(
  { token: Type.String({ minLength: 20, maxLength: 128 }) },
  { additionalProperties: false },
);

export const ForgotBody = Type.Object(
  { email: Type.String({ maxLength: 254 }) },
  { additionalProperties: false },
);

export const ResetBody = Type.Object(
  {
    token: Type.String({ minLength: 20, maxLength: 128 }),
    newPassword: Type.String({ minLength: 1, maxLength: 1024 }),
  },
  { additionalProperties: false },
);

export const AcceptedResponse = Type.Object({ status: Type.String() });
