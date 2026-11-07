import fastifyStatic from '@fastify/static';
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';
import type { FastifyInstance } from 'fastify';
import { ProblemError } from '../../lib/problem.js';

/**
 * Strict policy for the console: only same-origin scripts, styles and API calls; images may be
 * data: URLs (authenticator QR code). No inline scripts, no framing.
 */
export const ADMIN_CSP = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "connect-src 'self'",
  "font-src 'self'",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

/**
 * Serves the staff console SPA under /admin/ from the API process (same origin as /admin/v1, so
 * the SameSite=Strict, Path=/admin session cookie applies). Hashed assets are cached for a year;
 * index.html is never cached; unknown /admin/* paths return index.html (client-side routing),
 * except /admin/v1/* which stay API 404s.
 */
export async function registerAdminWeb(app: FastifyInstance, dir: string | undefined): Promise<void> {
  if (!dir) return;
  const root = resolve(dir);
  const indexFile = join(root, 'index.html');
  if (!existsSync(indexFile)) {
    app.log.warn({ dir: root }, 'ADMIN_WEB_DIR has no index.html; staff console not served');
    return;
  }
  const indexHtml = readFileSync(indexFile);

  await app.register(fastifyStatic, {
    root,
    prefix: '/admin/',
    wildcard: false,
    index: false,
    decorateReply: false,
    setHeaders(res, path) {
      const immutable = path.includes(`${sep}assets${sep}`);
      res.header('cache-control', immutable ? 'public, max-age=31536000, immutable' : 'no-cache');
      res.header('content-security-policy', ADMIN_CSP);
      res.header('x-frame-options', 'DENY');
    },
  });

  app.get('/admin', { schema: { hide: true } }, async (_request, reply) => reply.redirect('/admin/', 308));

  app.get('/admin/*', { schema: { hide: true } }, async (request, reply) => {
    const path = request.url.split('?')[0] ?? '';
    if (path.startsWith('/admin/v1/') || path.startsWith('/admin/assets/')) {
      throw ProblemError.notFound(`No route for GET ${path}`);
    }
    return reply
      .header('content-type', 'text/html; charset=utf-8')
      .header('cache-control', 'no-store')
      .header('content-security-policy', ADMIN_CSP)
      .header('x-frame-options', 'DENY')
      .send(indexHtml);
  });
}
