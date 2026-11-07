import { buildApp } from './app.js';
import { ConfigError, loadConfig, type AppConfig } from './config.js';
import { createRuntimeDeps } from './deps.js';

const SHUTDOWN_TIMEOUT_MS = 10_000;

async function main(): Promise<void> {
  let config: AppConfig;
  try {
    config = loadConfig();
  } catch (err) {
    if (err instanceof ConfigError) {
      // Logger is not configured yet; this is the one place stderr is used directly.
      process.stderr.write(`${err.message}\n`);
      process.exit(2);
    }
    throw err;
  }

  const deps = await createRuntimeDeps(config);
  const { redis } = deps;
  const app = await buildApp({ config, deps });
  for (const warning of deps.warnings) app.log.warn(warning);

  // Redis connects in the background; readiness reports it until it is reachable.
  redis.connect().catch((err: unknown) => app.log.warn({ err }, 'redis not reachable yet; retrying'));
  redis.on('error', (err) => app.log.debug({ err }, 'redis connection error'));

  let shuttingDown = false;
  const shutdown = (signal: string) => {
    if (shuttingDown) return;
    shuttingDown = true;
    app.log.info({ signal }, 'shutting down');
    const force = setTimeout(() => {
      app.log.error('graceful shutdown timed out; exiting');
      process.exit(1);
    }, SHUTDOWN_TIMEOUT_MS);
    force.unref();
    app
      .close()
      .then(() => process.exit(0))
      .catch((err: unknown) => {
        app.log.error({ err }, 'error during shutdown');
        process.exit(1);
      });
  };
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));

  await app.listen({ host: config.API_HOST, port: config.API_PORT });
  app.log.info({ version: config.BUILD_VERSION, env: config.APP_ENV }, 'kindling-api started');
}

main().catch((err: unknown) => {
  process.stderr.write(
    `Fatal startup error: ${err instanceof Error ? (err.stack ?? err.message) : String(err)}\n`,
  );
  process.exit(1);
});
