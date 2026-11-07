import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { call, client } from '../api/client';
import { StepUpDialog } from './step-up';

afterEach(() => vi.unstubAllGlobals());

const reply = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

describe('StepUpDialog', () => {
  it('pauses a dangerous request, asks for a code, then completes it', async () => {
    const calls: string[] = [];
    let rotations = 0;
    vi.stubGlobal('fetch', async (request: Request) => {
      const path = new URL(request.url, 'http://x').pathname;
      calls.push(`${request.method} ${path}`);
      if (path.endsWith('/step-up')) {
        const body = (await request.json()) as { totp: string };
        return body.totp === '123456'
          ? reply(200, { stepUpValidUntil: new Date().toISOString() })
          : reply(401, {
              title: 'Unauthorized',
              status: 401,
              code: 'staff.invalid_credentials',
              detail: 'The authenticator code is not valid.',
            });
      }
      rotations += 1;
      return rotations === 1
        ? reply(403, { title: 'Forbidden', status: 403, code: 'staff.step_up_required' })
        : reply(200, { token: 'kst_rotated' });
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <StepUpDialog />
      </QueryClientProvider>,
    );
    const user = userEvent.setup();
    const pending = call(() =>
      client.POST('/admin/v1/servers/{id}/token/rotate', {
        params: { path: { id: 's1' } },
        body: { reason: 'leaked' },
      }),
    );

    const input = await screen.findByLabelText('Authenticator code');
    await user.type(input, '000000');
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The authenticator code is not valid.');

    await user.clear(input);
    await user.type(input, '123456');
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    await expect(pending).resolves.toEqual({ token: 'kst_rotated' });
    await waitFor(() => expect(screen.queryByLabelText('Authenticator code')).not.toBeInTheDocument());
    expect(calls.filter((c) => c.includes('rotate'))).toHaveLength(2);
  });
});
