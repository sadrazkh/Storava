import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  page.on('pageerror', (error) => {
    console.error(`Browser page error: ${error.message}`);
  });
  page.on('console', (message) => {
    if (message.type() === 'error') {
      console.error(`Browser console error: ${message.text()}`);
    }
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

test('uses the browser permission surface and stops before scanning', async ({ page }) => {
  await page.getByRole('button', { name: 'Choose a folder' }).first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByText('Your browser is ready')).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByText('I understand that Storava works on this device.').click();
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByRole('button', { name: 'Open folder picker' }).click();
  await expect(page.getByText('Folder “Local Work” is ready. The scanner itself arrives in Phase 2.')).toBeVisible();
  await expect(page.getByText('No scan starts in Phase 1')).toBeVisible();
});

test('renders privacy and compatibility evidence without mock scan metrics', async ({ page }) => {
  await expect(page.getByText('Your files never leave your device.')).toBeVisible();
  await page.locator('#compatibility').scrollIntoViewIfNeeded();
  await expect(page.getByText('Native folder picker', { exact: true })).toBeVisible();
  await expect(page.getByText('Best experience')).toBeVisible();
  await expect(page.getByText(/files scanned|GB scanned|% complete/i)).toHaveCount(0);
});
