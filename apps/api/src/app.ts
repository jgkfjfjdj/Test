import { randomUUID } from 'node:crypto';
import cookie from '@fastify/cookie';
import rateLimit from '@fastify/rate-limit';
import swagger from '@fastify/swagger';
import websocket from '@fastify/websocket';
import { TypeBoxValidatorCompiler, type TypeBoxTypeProvider } from '@fastify/type-provider-typebox';
import { API_VERSION } from '@kindling/contracts';
import Fastify, { type FastifyInstance, type FastifyServerOptions } from 'fastify';
import type { AppConfig } from './config.js';
import { createContext, type AppContext, type AppDeps } from './context.js';
import { registerModules } from './modules/index.js';
import { registerErrorHandling } from './plugins/errors.js';

export type { AppDeps } from './context.js';

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
        'req.headers["x-csrf-token"]',
        'res.headers["set-cookie"]',
        '*.password',
        '*.newPassword',
        '*.token',
        '*.refreshToken',
        '*.accessToken',
        '*.identityToken',
        '*.totp',
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

declare module 'fastify' {
  interface FastifyInstance {
    ctx: AppContext;
  }
}

export async function buildApp(options: BuildAppOptions): Promise<FastifyInstance> {
  const { config, deps } = options;
  const startedAt = new Date();
  const ctx = createContext(config, deps);

  const app = Fastify({
    logger: options.logger ?? loggerOptions(config),
    trustProxy: config.TRUST_PROXY,
    bodyLimit: config.BODY_LIMIT_BYTES,
    genReqId: (req) => {
      const incoming = req.headers['x-request-id'];
      return typeof incoming === 'string' && SAFE_REQUEST_ID.test(incoming) ? incoming : randomUUID();
    },
  }).withTypeProvider<TypeBoxTypeProvider>();

  app.decorate('ctx', ctx);
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

  await app.register(cookie);
  await app.register(rateLimit, {
    global: false,
    redis: deps.redis,
    nameSpace: 'rl:',
    // Abuse protection must not take the API down with Redis: allow the request and log.
    skipOnError: true,
  });
  await app.register(websocket, { options: { maxPayload: 64 * 1024 } });

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
      tags: [
        { name: 'health', description: 'Liveness, readiness and build information' },
        { name: 'bootstrap', description: 'Version gate, maintenance and remote config at app start' },
        { name: 'auth', description: 'Player registration, login and sessions' },
        { name: 'me', description: 'The signed-in player: profile, cloud saves, account deletion' },
        { name: 'servers', description: 'Server browser and joining' },
        { name: 'server-link', description: 'Game server ↔ backend (server token auth)' },
        { name: 'admin', description: 'Staff console (cookie session + CSRF)' },
      ],
      components: {
        securitySchemes: {
          bearer: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' },
          serverToken: { type: 'http', scheme: 'bearer', description: 'Game server token' },
          staffSession: { type: 'apiKey', in: 'cookie', name: 'kindling_staff' },
        },
      },
    },
  });

  await registerModules(app, { startedAt, probes: deps.probes });

  return app;
}
