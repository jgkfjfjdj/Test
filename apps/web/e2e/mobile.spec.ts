import { expect, test } from '@playwright/test';
import { setupToken, shot } from './helpers';

/** Phone layout (iPhone 14 viewport + touch). Reuses the session created by the desktop project. */
test('navigation drawer, card lists and dialogs work on a phone', async ({ page }) => {
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: /Hello, Avery/ })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden(); // sidebar collapsed
  await page.screenshot({ path: shot('iphone-1-overview.png'), fullPage: true });

  await page.getByRole('button', { name: 'Open menu' }).tap();
  const menu = page.getByRole('dialog', { name: 'Menu' });
  await expect(menu.getByRole('link', { name: 'Servers' })).toBeVisible();
  await page.screenshot({ path: shot('iphone-2-menu.png') });
  await menu.getByRole('link', { name: 'Servers' }).tap();
  await expect(menu).toBeHidden();

  // Phones get cards instead of a table.
  const cards = page.getByRole('list', { name: 'Servers' });
  await expect(cards.getByText('Official EU Playtest')).toBeVisible();
  await expect(page.getByRole('table')).toBeHidden();
  await page.screenshot({ path: shot('iphone-3-servers.png'), fullPage: true });

  await page.getByRole('button', { name: 'Open menu' }).tap();
  await page.getByRole('dialog', { name: 'Menu' }).getByRole('link', { name: 'Maintenance' }).tap();
  await page.getByRole('button', { name: 'Schedule maintenance' }).tap();
  await expect(page.getByRole('dialog', { name: 'Schedule maintenance' })).toBeVisible();
  await page.screenshot({ path: shot('iphone-4-dialog.png') });

  // No horizontal scrolling at phone width.
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
});

test('sign-in and setup pages fit a phone screen', async ({ browser }) => {
  const context = await browser.newContext({ ...test.info().project.use, storageState: undefined });
  const page = await context.newPage();
  await page.goto('/admin/login');
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await page.screenshot({ path: shot('iphone-5-login.png') });
  await page.goto(`/admin/setup?token=${setupToken()}`);
  await expect(page.getByRole('heading', { name: 'Setup link not valid' })).toBeVisible(); // already used by the desktop run
  await context.close();
});
