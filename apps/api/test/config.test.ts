import { describe, expect, it } from 'vitest';
import { ConfigError, loadConfig } from '../src/config.js';

const required = { DATABASE_URL: 'postgres://u:p@h/db', REDIS_URL: 'redis://h:6379' };

describe('loadConfig', () => {
  it('applies defaults', () => {
    const config = loadConfig(required);
    expect(config.API_PORT).toBe(8080);
    expect(config.API_HOST).toBe('0.0.0.0');
    expect(config.NODE_ENV).toBe('development');
    expect(config.LOG_LEVEL).toBe('info');
    expect(config.TRUST_PROXY).toBe(false);
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
});
