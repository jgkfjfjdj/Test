import { spawn, type ChildProcess } from 'node:child_process';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { ADMIN, setupToken, shot, totp } from './helpers';

/**
 * One staff member's full journey through the console, against the real API and database:
 * invitation setup → sign-in → servers (incl. a live fake game server) → maintenance →
 * remote config → app versions → staff → audit log. Runs serially in one browser session.
 */
test.describe.configure({ mode: 'serial' });

let context: BrowserContext;
let page: Page;
let secret = '';
let fakeServer: ChildProcess | undefined;
const SERVER_NAME = 'Official EU Playtest';

/** Clicks a link in the sidebar navigation. */
async function go(label: string) {
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: label, exact: true })
    .click();
}

test.beforeAll(async ({ browser }) => {
  context = await browser.newContext();
  page = await context.newPage();
});

test.afterAll(async () => {
  fakeServer?.kill('SIGINT');
  await context.close();
});

test('invitation setup: authenticator enrollment and password', async () => {
  await page.goto(`/admin/setup?token=${setupToken()}`);
  await expect(page.getByRole('heading', { name: 'Set up your staff account' })).toBeVisible();
  await expect(page.getByRole('img', { name: /QR code/ })).toBeVisible();
  await page.screenshot({ path: shot('desktop-1-setup.png'), fullPage: true });
  secret = ((await page.getByTestId('totp-secret').textContent()) ?? '').replace(/\s+/g, '');
  expect(secret).toMatch(/^[A-Z2-7]{32}$/);

  await page.getByLabel('Password', { exact: true }).fill(ADMIN.password);
  await page.getByLabel('Repeat password').fill('a different password!!');
  await expect(page.getByText('The passwords are different.')).toBeVisible();
  await page.getByLabel('Repeat password').fill(ADMIN.password);
  await page.getByLabel('Code from the authenticator app').fill(totp(secret));
  await page.getByRole('button', { name: 'Activate account' }).click();
  await expect(page.getByRole('heading', { name: "You're all set" })).toBeVisible();
});

test('sign-in redirects back to the requested page', async () => {
  await page.goto('/admin/servers');
  await expect(page).toHaveURL(/\/admin\/login\?redirect=%2Fservers/);
  await page.getByLabel('Email').fill(ADMIN.email);
  await page.getByLabel('Password').fill('wrong password here');
  await page.getByLabel('Authenticator code').fill(totp(secret, 1));
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('incorrect');
  await page.screenshot({ path: shot('desktop-2-login-error.png') });

  await page.getByLabel('Password').fill(ADMIN.password);
  await page.getByLabel('Authenticator code').fill(totp(secret, 1));
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Servers' })).toBeVisible();
  await expect(page).toHaveURL(/\/admin\/servers$/);
  await context.storageState({ path: 'e2e/.auth/staff.json' });
});

test('servers: register, reveal the token once, go live, send a command', async () => {
  await page.getByRole('button', { name: 'Register server' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name').fill(SERVER_NAME);
  await dialog.getByLabel('Max players').fill('60');
  await dialog.getByLabel('Reason').fill('playtest fleet');
  await dialog.getByRole('button', { name: 'Register server' }).click();
  await expect(dialog.getByText('Shown only once')).toBeVisible();
  const token = (await dialog.getByLabel('Server token', { exact: true }).textContent()) ?? '';
  expect(token).toMatch(/^kst_/);
  await page.screenshot({ path: shot('desktop-3-server-token.png') });
  await dialog.getByRole('button', { name: "I've stored the token" }).click();

  const row = page.getByRole('row', { name: new RegExp(SERVER_NAME) });
  await expect(row.getByText('Offline')).toBeVisible();

  // A stand-in game server connects with the token and reports 3 players.
  fakeServer = spawn(
    'pnpm',
    [
      '--silent',
      '--filter',
      '@kindling/api',
      'fake-server',
      '--',
      '--token',
      token,
      '--url',
      'http://127.0.0.1:8099',
      '--players',
      '3',
    ],
    {
      stdio: 'ignore',
    },
  );
  await expect(row.getByText('Online')).toBeVisible({ timeout: 20_000 });
  await expect(row.getByRole('cell', { name: '3 / 50' })).toBeVisible();
  await page.screenshot({ path: shot('desktop-4-servers.png'), fullPage: true });

  await row.getByRole('link', { name: SERVER_NAME }).click();
  await expect(page.getByRole('heading', { name: SERVER_NAME })).toBeVisible();
  await expect(page.getByText('0.0.0-fake')).toBeVisible();
  await page.getByLabel('Message', { exact: true }).fill('Server restarts in 10 minutes');
  await page.getByLabel('Reason').fill('weekly restart');
  await page.getByRole('button', { name: 'Send' }).click();
  await expect(page.getByText('"handledBy": "fake-game-server"')).toBeVisible();
  await page.screenshot({ path: shot('desktop-5-server-detail.png'), fullPage: true });
});

test('maintenance: start immediately, see it on the overview, end it', async () => {
  await go('Maintenance');
  await page.getByRole('button', { name: 'Schedule maintenance' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('What is affected').selectOption('login');
  await dialog
    .getByLabel('Message to players (English)')
    .fill('Sign-in is paused for a quick database upgrade.');
  await dialog.getByLabel('Reason').fill('db upgrade');
  await dialog.getByRole('button', { name: 'Start now' }).click();
  await expect(page.getByRole('row', { name: /Sign-in/ }).getByText('active')).toBeVisible();

  await go('Overview');
  await expect(page.getByText('Maintenance in progress')).toBeVisible();
  await expect(page.getByText('3', { exact: true })).toBeVisible(); // players in game
  await page.screenshot({ path: shot('desktop-6-overview.png'), fullPage: true });

  await go('Maintenance');
  await page
    .getByRole('row', { name: /Sign-in/ })
    .getByRole('button', { name: 'End now' })
    .click();
  await page.getByRole('dialog').getByLabel('Reason').fill('upgrade finished');
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click();
  await expect(page.getByRole('row', { name: /Sign-in/ }).getByText('cancelled')).toBeVisible();
});

test('remote config: validate JSON, create, edit (version bump)', async () => {
  await go('Remote config');
  await page.getByRole('button', { name: 'New entry' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Key').fill('feature.voiceChat');
  await dialog.getByLabel('Value (JSON)').fill('{oops');
  await expect(dialog.getByText(/Not valid JSON/)).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Save' })).toBeDisabled();
  await dialog.getByLabel('Value (JSON)').fill('false');
  await dialog.getByLabel('Reason').fill('voice off until moderation is ready');
  await dialog.getByRole('button', { name: 'Save' }).click();
  const row = page.getByRole('row', { name: /feature\.voiceChat/ });
  await expect(row.getByText('v1')).toBeVisible();

  await row.getByRole('button', { name: 'Edit feature.voiceChat' }).click();
  await page.getByRole('dialog').getByLabel('Value (JSON)').fill('true');
  await page.getByRole('dialog').getByLabel('Reason').fill('closed beta test');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(row.getByText('v2')).toBeVisible();
  await expect(row.getByText('true')).toBeVisible();
  await page.screenshot({ path: shot('desktop-7-config.png'), fullPage: true });
});

test('app versions: raise the minimum build', async () => {
  await go('App versions');
  await page.getByRole('button', { name: 'Edit' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Minimum supported build').fill('120');
  await dialog.getByLabel('Latest build').fill('100');
  await expect(dialog.getByText('Cannot be above the latest build.')).toBeVisible();
  await dialog.getByLabel('Latest build').fill('130');
  await dialog.getByLabel('App Store URL').fill('https://apps.apple.com/app/id0000000000');
  await dialog.getByLabel('Reason').fill('1.1 release');
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Version policy updated')).toBeVisible();
  await expect(page.getByText('130', { exact: true })).toBeVisible();
});

test('staff: invite a moderator and get the setup link', async () => {
  await go('Staff');
  await page.getByRole('button', { name: 'Invite staff' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name').fill('Morgan Mod');
  await dialog.getByLabel('Email').fill('morgan@studio.example');
  await dialog.getByRole('checkbox', { name: /Moderator/ }).check();
  await dialog.getByLabel('Reason').fill('weekend moderation cover');
  await dialog.getByRole('button', { name: 'Send invitation' }).click();
  await expect(dialog.getByText('Invitation sent')).toBeVisible();
  await expect(dialog.getByLabel('Setup link', { exact: true })).toContainText('/admin/setup?token=');
  await dialog.getByRole('button', { name: 'Done' }).click();
  const row = page.getByRole('row', { name: /Morgan Mod/ });
  await expect(row.getByText('invited')).toBeVisible();
  await expect(page.getByRole('row', { name: /Avery Lead/ }).getByText('you')).toBeVisible();
  await page.screenshot({ path: shot('desktop-8-staff.png'), fullPage: true });
});

test('audit log: every change above is recorded with its reason', async () => {
  await go('Audit log');
  for (const action of [
    'staff.invite',
    'version_policy.update',
    'config.update',
    'maintenance.cancel',
    'server.command.broadcast',
    'server.create',
  ]) {
    await expect(
      page.getByRole('list', { name: 'Audit entries' }).getByText(action, { exact: true }).first(),
    ).toBeVisible();
  }
  await page.getByLabel('Action').fill('config.update');
  await page.getByRole('button', { name: 'Filter' }).click();
  await expect(page).toHaveURL(/action=config\.update/);
  const entries = page.getByRole('list', { name: 'Audit entries' }).getByRole('listitem');
  await expect(entries).toHaveCount(2);
  await entries.first().locator('summary').click();
  await expect(entries.first().getByText('Changed:')).toBeVisible();
  await expect(entries.first().getByText('closed beta test')).toBeVisible();
  await page.screenshot({ path: shot('desktop-9-audit.png'), fullPage: true });
});
