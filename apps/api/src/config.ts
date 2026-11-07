import { Type, type Static } from 'typebox';
import { Value } from 'typebox/value';

const LOG_LEVELS = ['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent'] as const;

/** Environment variables understood by the API process. Everything else in process.env is ignored. */
export const ConfigSchema = Type.Object({
  NODE_ENV: Type.Union([Type.Literal('development'), Type.Literal('test'), Type.Literal('production')], {
    default: 'development',
  }),
  /** Deployment environment name: local | dev | staging | production. */
  APP_ENV: Type.String({ default: 'local', minLength: 1 }),
  API_HOST: Type.String({ default: '0.0.0.0' }),
  API_PORT: Type.Integer({ minimum: 1, maximum: 65535, default: 8080 }),
  LOG_LEVEL: Type.Union(
    LOG_LEVELS.map((l) => Type.Literal(l)),
    { default: 'info' },
  ),
  DATABASE_URL: Type.String({ minLength: 1 }),
  REDIS_URL: Type.String({ minLength: 1 }),
  DB_POOL_MAX: Type.Integer({ minimum: 1, maximum: 200, default: 10 }),
  /** Set to true only behind a trusted reverse proxy (Caddy) so client IPs come from X-Forwarded-For. */
  TRUST_PROXY: Type.Boolean({ default: false }),
  BODY_LIMIT_BYTES: Type.Integer({ minimum: 1024, default: 1_048_576 }),
  BUILD_VERSION: Type.String({ default: '0.0.0-dev' }),
  BUILD_COMMIT: Type.String({ default: 'unknown' }),

  /** Base URL used in emailed links (verification, password reset, staff invites). */
  PUBLIC_BASE_URL: Type.String({ default: 'http://localhost:8080' }),

  // --- Signing keys (JSON array of private Ed25519 JWKs with "kid"; first = active, rest = verify-only).
  // Missing outside production → an ephemeral key is generated at startup (tokens die on restart).
  ACCESS_TOKEN_KEYS: Type.Optional(Type.String()),
  TICKET_KEYS: Type.Optional(Type.String()),
  ACCESS_TOKEN_TTL_SECONDS: Type.Integer({ minimum: 60, maximum: 3600, default: 900 }),
  REFRESH_TOKEN_TTL_DAYS: Type.Integer({ minimum: 1, maximum: 365, default: 60 }),
  JOIN_TICKET_TTL_SECONDS: Type.Integer({ minimum: 10, maximum: 300, default: 60 }),
  /** How long an admin command waits for the game server's answer. */
  SERVER_COMMAND_TIMEOUT_MS: Type.Integer({ minimum: 100, maximum: 60_000, default: 10_000 }),

  // --- Secrets (required in production; development uses fixed, clearly insecure defaults).
  /** HMAC key for hashing IP addresses before storage (base64, ≥ 32 bytes). */
  IP_HASH_SECRET: Type.Optional(Type.String()),
  /** AES-256-GCM key protecting staff TOTP secrets (base64, exactly 32 bytes). */
  STAFF_SECRET_KEY: Type.Optional(Type.String()),

  // --- Sign in with Apple
  /** Comma-separated client ids (iOS bundle ids / Services IDs) accepted as token audience. */
  APPLE_CLIENT_IDS: Type.String({ default: 'com.example.kindling' }),

  // --- Email
  MAIL_TRANSPORT: Type.Union([Type.Literal('log'), Type.Literal('smtp')], { default: 'log' }),
  SMTP_URL: Type.Optional(Type.String()),
  MAIL_FROM: Type.String({ default: 'KINDLING <no-reply@kindling.invalid>' }),

  // --- Web console sessions
  /** Secure cookies require HTTPS; set false only for local http development. */
  ADMIN_COOKIE_SECURE: Type.Boolean({ default: true }),
  STAFF_SESSION_HOURS: Type.Integer({ minimum: 1, maximum: 24, default: 8 }),
  STAFF_IDLE_MINUTES: Type.Integer({ minimum: 5, maximum: 240, default: 30 }),

  // --- Abuse protection
  /** Multiplies every rate limit (tests raise it; never lower it below 1 in production). */
  RATE_LIMIT_MULTIPLIER: Type.Number({ minimum: 1, default: 1 }),
  /** Check new passwords against the Have I Been Pwned k-anonymity range API. */
  PASSWORD_BREACH_CHECK: Type.Boolean({ default: false }),
});

export type AppConfig = Static<typeof ConfigSchema>;

export class ConfigError extends Error {
  constructor(public readonly problems: string[]) {
    super(`Invalid configuration:\n  - ${problems.join('\n  - ')}`);
    this.name = 'ConfigError';
  }
}

function base64Length(value: string): number {
  return Buffer.from(value, 'base64').length;
}

/**
 * Builds a validated config from an env-like record. Unknown keys are dropped,
 * defaults applied, strings converted ("8080" → 8080, "true" → true).
 */
export function loadConfig(env: Record<string, string | undefined> = process.env): AppConfig {
  const raw: Record<string, unknown> = {};
  for (const key of Object.keys(ConfigSchema.properties)) {
    const value = env[key];
    if (value !== undefined && value !== '') raw[key] = value;
  }
  const withDefaults = Value.Default(ConfigSchema, raw);
  const converted = Value.Convert(ConfigSchema, withDefaults);
  if (!Value.Check(ConfigSchema, converted)) {
    const problems = [...Value.Errors(ConfigSchema, converted)].map((e) => {
      const params = e.params as Record<string, unknown> | undefined;
      const missing = Array.isArray(params?.['requiredProperties'])
        ? params['requiredProperties'].join(', ')
        : '';
      const key = e.instancePath.replace(/^\//, '') || missing || '(root)';
      return `${key}: ${e.message}`;
    });
    throw new ConfigError(problems);
  }
  const config = converted as AppConfig;

  const problems: string[] = [];
  if (config.NODE_ENV === 'production') {
    for (const key of ['ACCESS_TOKEN_KEYS', 'TICKET_KEYS', 'IP_HASH_SECRET', 'STAFF_SECRET_KEY'] as const) {
      if (!config[key]) problems.push(`${key}: required in production`);
    }
    if (!config.ADMIN_COOKIE_SECURE) problems.push('ADMIN_COOKIE_SECURE: must be true in production');
    if (config.MAIL_TRANSPORT !== 'smtp') problems.push('MAIL_TRANSPORT: must be "smtp" in production');
  }
  if (config.IP_HASH_SECRET && base64Length(config.IP_HASH_SECRET) < 32) {
    problems.push('IP_HASH_SECRET: must be base64 of at least 32 bytes');
  }
  if (config.STAFF_SECRET_KEY && base64Length(config.STAFF_SECRET_KEY) !== 32) {
    problems.push('STAFF_SECRET_KEY: must be base64 of exactly 32 bytes');
  }
  if (config.MAIL_TRANSPORT === 'smtp' && !config.SMTP_URL) {
    problems.push('SMTP_URL: required when MAIL_TRANSPORT is "smtp"');
  }
  if (problems.length > 0) throw new ConfigError(problems);
  return config;
}

/** Fixed development secrets. They are public (in the repo), so production refuses to start without real ones. */
export const DEV_IP_HASH_SECRET = Buffer.from('kindling-development-ip-hash-secret-not-for-prod!').toString(
  'base64',
);
export const DEV_STAFF_SECRET_KEY = Buffer.from('kindling-dev-staff-key-NOT-PROD!').toString('base64');
