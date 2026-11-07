import { describe, expect, it } from 'vitest';
import { ConfigError, loadConfig } from '../src/config.js';

const required = { DATABASE_URL: 'postgres://u:p@h/db', REDIS_URL: 'redis://h:6379' };
const key32 = Buffer.alloc(32, 7).toString('base64');

describe('loadConfig', () => {
  it('applies defaults', () => {
    const config = loadConfig(required);
    expect(config.API_PORT).toBe(8080);
    expect(config.API_HOST).toBe('0.0.0.0');
    expect(config.NODE_ENV).toBe('development');
    expect(config.LOG_LEVEL).toBe('info');
    expect(config.TRUST_PROXY).toBe(false);
    expect(config.ACCESS_TOKEN_TTL_SECONDS).toBe(900);
    expect(config.JOIN_TICKET_TTL_SECONDS).toBe(60);
  });

  it('converts strings to numbers and booleans', () => {
    const config = loadConfig({ ...required, API_PORT: '9000', TRUST_PROXY: 'true', DB_POOL_MAX: '25' });
    expect(config.API_PORT).toBe(9000);
    expect(config.TRUST_PROXY).toBe(true);
    expect(config.DB_POOL_MAX).toBe(25);
  });

  it('ignores unknown environment variables', () => {
    const config = loadConfig({ ...required, SOMETHING_ELSE: 'x' }) as Record<string, unknown>;
    expect(config['SOMETHING_ELSE']).toBeUndefined();
  });

  it('rejects missing required values with a readable error', () => {
    expect(() => loadConfig({ REDIS_URL: 'redis://h' })).toThrow(ConfigError);
    try {
      loadConfig({ REDIS_URL: 'redis://h' });
    } catch (err) {
      expect((err as ConfigError).message).toContain('DATABASE_URL');
    }
  });

  it('rejects out-of-range and invalid values', () => {
    expect(() => loadConfig({ ...required, API_PORT: '70000' })).toThrow(ConfigError);
    expect(() => loadConfig({ ...required, LOG_LEVEL: 'loud' })).toThrow(ConfigError);
    expect(() => loadConfig({ ...required, NODE_ENV: 'prod' })).toThrow(ConfigError);
  });

  it('refuses to start in production without real secrets and secure cookies', () => {
    try {
      loadConfig({ ...required, NODE_ENV: 'production', ADMIN_COOKIE_SECURE: 'false' });
      expect.unreachable();
    } catch (err) {
      const message = (err as ConfigError).message;
      for (const key of [
        'ACCESS_TOKEN_KEYS',
        'TICKET_KEYS',
        'IP_HASH_SECRET',
        'STAFF_SECRET_KEY',
        'ADMIN_COOKIE_SECURE',
        'MAIL_TRANSPORT',
      ]) {
        expect(message).toContain(key);
      }
    }
  });

  it('accepts a complete production configuration', () => {
    const config = loadConfig({
      ...required,
      NODE_ENV: 'production',
      ACCESS_TOKEN_KEYS: '[]',
      TICKET_KEYS: '[]',
      IP_HASH_SECRET: key32,
      STAFF_SECRET_KEY: key32,
      MAIL_TRANSPORT: 'smtp',
      SMTP_URL: 'smtp://mail:25',
    });
    expect(config.NODE_ENV).toBe('production');
  });

  it('validates secret lengths', () => {
    expect(() => loadConfig({ ...required, STAFF_SECRET_KEY: Buffer.alloc(16).toString('base64') })).toThrow(
      /STAFF_SECRET_KEY/,
    );
    expect(() => loadConfig({ ...required, IP_HASH_SECRET: Buffer.alloc(8).toString('base64') })).toThrow(
      /IP_HASH_SECRET/,
    );
    expect(() => loadConfig({ ...required, MAIL_TRANSPORT: 'smtp' })).toThrow(/SMTP_URL/);
  });
});
