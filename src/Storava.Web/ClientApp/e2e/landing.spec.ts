import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  page.on('pageerror', (error) => console.error(`Browser page error: ${error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') console.error(`Browser console error: ${message.text()}`);
  });
  await page.addInitScript(() => {
    Object.defineProperty(window, 'showDirectoryPicker', {
      configurable: true,
      value: () => Promise.resolve({
        kind: 'directory',
        name: 'Local Work',
        requestPermission: () => Promise.resolve('granted'),
      }),
    });
  });
  await page.goto('/');
});

test('changes language direction and theme without navigating', async ({ page }) => {
  await expect(page.locator('h1')).toContainText('Find what is');
  await page.getByRole('button', { name: 'فا' }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('h1')).toContainText('ببینید چه چیزی');
  await page.getByRole('button', { name: 'تاریک' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page).toHaveURL('/');
});

test('routes the primary scan action to the real local workspace', async ({ page }) => {
  await page.getByRole('link', { name: 'Choose a folder' }).first().click();
  await expect(page).toHaveURL('/scan');
  await expect(page.getByRole('heading', { name: 'Choose a folder to scan' })).toBeVisible();
  await expect(page.getByText('Your files never leave your device.').first()).toBeVisible();
});

test('renders privacy and compatibility evidence without mock scan metrics', async ({ page }) => {
  await expect(page.getByText('Your files never leave your device.')).toBeVisible();
  await page.locator('#compatibility').scrollIntoViewIfNeeded();
  await expect(page.getByText('Native folder picker', { exact: true })).toBeVisible();
  await expect(page.getByText('Best experience')).toBeVisible();
  await expect(page.getByText(/files scanned|GB scanned|% complete/i)).toHaveCount(0);
});
