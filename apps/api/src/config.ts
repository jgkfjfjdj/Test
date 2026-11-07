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
});

export type AppConfig = Static<typeof ConfigSchema>;

export class ConfigError extends Error {
  constructor(public readonly problems: string[]) {
    super(`Invalid configuration:\n  - ${problems.join('\n  - ')}`);
    this.name = 'ConfigError';
  }
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
  return converted as AppConfig;
}
