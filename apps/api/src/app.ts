import { randomUUID } from 'node:crypto';
import swagger from '@fastify/swagger';
import { TypeBoxValidatorCompiler, type TypeBoxTypeProvider } from '@fastify/type-provider-typebox';
import { API_VERSION } from '@kindling/contracts';
import Fastify, { type FastifyInstance, type FastifyServerOptions } from 'fastify';
import type { AppConfig } from './config.js';
import { healthRoutes, type Pingable } from './modules/health/routes.js';
import { registerErrorHandling } from './plugins/errors.js';

export interface AppDeps {
  /** Dependency probes used by the readiness endpoint. */
  probes: { postgres: Pingable; redis: Pingable };
  /** Called once when the server closes (release pools/connections). */
  close?: () => Promise<void>;
}

export interface BuildAppOptions {
  config: AppConfig;
  deps: AppDeps;
  /** Override logger config (tests pass `false`). */
  logger?: FastifyServerOptions['logger'];
}

/** Incoming request ids are accepted only if they are short and log-safe. */
const SAFE_REQUEST_ID = /^[A-Za-z0-9._-]{8,64}$/;

export function loggerOptions(config: AppConfig): FastifyServerOptions['logger'] {
  return {
    level: config.LOG_LEVEL,
    redact: {
      paths: [
        'req.headers.authorization',
        'req.headers.cookie',
        'req.headers["x-server-token"]',
        '*.password',
        '*.token',
        '*.refreshToken',
        '*.accessToken',
      ],
      censor: '[redacted]',
    },
    ...(config.NODE_ENV === 'development' && process.stdout.isTTY
      ? {
          transport: {
            target: 'pino-pretty',
            options: { translateTime: 'HH:MM:ss.l', ignore: 'pid,hostname' },
          },
        }
      : {}),
  };
}

export async function buildApp(options: BuildAppOptions): Promise<FastifyInstance> {
  const { config, deps } = options;
  const startedAt = new Date();

  const app = Fastify({
    logger: options.logger ?? loggerOptions(config),
    trustProxy: config.TRUST_PROXY,
    bodyLimit: config.BODY_LIMIT_BYTES,
    genReqId: (req) => {
      const incoming = req.headers['x-request-id'];
      return typeof incoming === 'string' && SAFE_REQUEST_ID.test(incoming) ? incoming : randomUUID();
    },
  }).withTypeProvider<TypeBoxTypeProvider>();

  app.setValidatorCompiler(TypeBoxValidatorCompiler);

  app.addHook('onSend', async (request, reply) => {
    reply.header('x-request-id', request.id);
    reply.header('x-content-type-options', 'nosniff');
    reply.header('referrer-policy', 'no-referrer');
    if (!reply.hasHeader('cache-control')) reply.header('cache-control', 'no-store');
  });

  if (deps.close) {
    const close = deps.close;
    app.addHook('onClose', async () => {
      await close();
    });
  }

  registerErrorHandling(app);

  await app.register(swagger, {
    openapi: {
      openapi: '3.1.0',
      info: {
        title: 'KINDLING Platform API',
        version: API_VERSION,
        description:
          'REST API for the KINDLING game client, game servers and web console. ' +
          'All errors use RFC 9457 problem+json with a stable `code`.',
      },
      tags: [{ name: 'health', description: 'Liveness, readiness and build information' }],
    },
  });

  await app.register(healthRoutes, { config, probes: deps.probes, startedAt });

  return app;
}
