import type { FastifyPluginAsync, FastifyReply } from 'fastify';
import { ProblemError } from '../../lib/problem.js';
import { rateLimit } from '../../lib/rate-limit.js';
import { AuthService } from './service.js';

/**
 * Minimal server-rendered pages for the links in account emails (no JavaScript, strict CSP).
 * They exist so verification and password reset work before any player-facing website does.
 */
function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!,
  );
}

function page(reply: FastifyReply, status: number, title: string, body: string): FastifyReply {
  return reply
    .status(status)
    .type('text/html; charset=utf-8')
    .header(
      'content-security-policy',
      "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'",
    )
    .header('x-frame-options', 'DENY')
    .send(
      `<!doctype html><html lang="en"><head><meta charset="utf-8">` +
        `<meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex">` +
        `<title>${escapeHtml(title)} · KINDLING</title>` +
        `<style>body{font:16px/1.5 system-ui,sans-serif;max-width:28rem;margin:3rem auto;padding:0 1rem;color:#1b1b1b;background:#fafafa}` +
        `@media(prefers-color-scheme:dark){body{color:#eee;background:#141414}input{background:#222;color:#eee}}` +
        `input,button{font:inherit;width:100%;padding:.6rem;margin:.3rem 0 1rem;box-sizing:border-box;border-radius:6px;border:1px solid #888}` +
        `button{cursor:pointer;background:#c2410c;color:#fff;border:0}</style></head>` +
        `<body><h1>${escapeHtml(title)}</h1>${body}</body></html>`,
    );
}

function messageOf(err: unknown): string {
  return err instanceof ProblemError
    ? (err.detail ?? err.title)
    : 'Something went wrong. Please try again later.';
}

export const accountPages: FastifyPluginAsync = async (app) => {
  const { config } = app.ctx;
  const auth = new AuthService(app.ctx);

  // HTML forms post urlencoded bodies; parse them only inside this plugin.
  app.addContentTypeParser(
    'application/x-www-form-urlencoded',
    { parseAs: 'string', bodyLimit: 4096 },
    (_req, body, done) => {
      done(null, Object.fromEntries(new URLSearchParams(body as string)));
    },
  );

  app.get<{ Querystring: { token?: string } }>(
    '/account/verify-email',
    { schema: { hide: true }, config: rateLimit(config, 'page-verify', 30, '1 hour') },
    async (request, reply) => {
      try {
        await auth.verifyEmail(String(request.query.token ?? ''));
        return page(
          reply,
          200,
          'Email confirmed',
          '<p>Thanks! Your email address is confirmed. You can return to the game.</p>',
        );
      } catch (err) {
        return page(
          reply,
          400,
          'Link not valid',
          `<p>${escapeHtml(messageOf(err))}</p><p>You can request a new link from the game's account settings.</p>`,
        );
      }
    },
  );

  app.get<{ Querystring: { token?: string } }>(
    '/account/reset-password',
    { schema: { hide: true }, config: rateLimit(config, 'page-reset', 30, '1 hour') },
    async (request, reply) => {
      const token = String(request.query.token ?? '');
      if (!/^[A-Za-z0-9_-]{20,128}$/.test(token)) {
        return page(
          reply,
          400,
          'Link not valid',
          '<p>This link is invalid. Request a new one from the game.</p>',
        );
      }
      return page(
        reply,
        200,
        'Choose a new password',
        `<form method="post" action="/account/reset-password">` +
          `<input type="hidden" name="token" value="${escapeHtml(token)}">` +
          `<label>New password (at least 10 characters)<input type="password" name="password" autocomplete="new-password" minlength="10" maxlength="128" required></label>` +
          `<label>Repeat new password<input type="password" name="confirm" autocomplete="new-password" minlength="10" maxlength="128" required></label>` +
          `<button type="submit">Set new password</button></form>`,
      );
    },
  );

  app.post<{ Body: Record<string, string> }>(
    '/account/reset-password',
    { schema: { hide: true }, config: rateLimit(config, 'page-reset-post', 10, '1 hour') },
    async (request, reply) => {
      const { token = '', password = '', confirm = '' } = request.body ?? {};
      if (password !== confirm) {
        return page(
          reply,
          400,
          'Passwords differ',
          '<p>The two passwords were not the same. Go back and try again.</p>',
        );
      }
      try {
        await auth.resetPassword(token, password);
        return page(
          reply,
          200,
          'Password changed',
          '<p>Your password was changed and you were signed out everywhere. Sign in again in the game.</p>',
        );
      } catch (err) {
        return page(reply, 400, 'Password not changed', `<p>${escapeHtml(messageOf(err))}</p>`);
      }
    },
  );
};
