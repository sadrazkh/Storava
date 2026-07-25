import { expect, test, type Page } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

async function forceFallback(page: Page): Promise<void> {
  await page.addInitScript(() => {
    Object.defineProperty(window, 'showDirectoryPicker', { configurable: true, value: undefined });
  });
}

async function selectDirectory(page: Page, directory: string): Promise<void> {
  const chooserPromise = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: /Choose folder & scan|انتخاب پوشه و شروع اسکن/ }).click();
  const chooser = await chooserPromise;
  await chooser.setFiles(directory);
}

test.beforeEach(async ({ page }) => {
  await forceFallback(page);
  page.on('pageerror', (error) => console.error(error));
  await page.goto('/scan');
});

test('scans real browser File objects, persists results, and exports/imports them', async ({ page }, testInfo) => {
  const directory = testInfo.outputPath('small-folder');
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'report.pdf'), Buffer.alloc(128, 1));
  await writeFile(join(directory, 'archive.zip'), Buffer.alloc(256, 2));
  await writeFile(join(directory, 'notes.txt'), Buffer.alloc(64, 3));
  await selectDirectory(page, directory);

  await expect(page.getByText(/Scan complete|اسکن کامل شد/)).toBeVisible();
  await expect(page.getByText('3', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: /Explorer|کاوشگر/ }).click();
  await expect(page.getByText('archive.zip', { exact: true }).first()).toBeVisible();

  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: /Export|خروجی/ }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/\.storava-web$/);
  const exportedPath = await download.path();
  expect(exportedPath).toBeTruthy();

  await page.getByRole('button', { name: /History|تاریخچه/ }).click();
  const before = await page.locator('.history-card').count();
  const fileChooserPromise = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: /Import|ورود/ }).click();
  const importer = await fileChooserPromise;
  await importer.setFiles(exportedPath);
  await expect(page.getByText(/Import complete|ورود کامل شد/)).toBeVisible();
  await expect(page.locator('.history-card')).toHaveCount(before + 1);
});

test('wires pause, resume, and cancel commands to the worker lifecycle', async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    class ControlledWorker extends EventTarget {
      postMessage(command: { type: string }): void {
        const dispatch = (data: unknown) => this.dispatchEvent(new MessageEvent('message', { data }));
        if (command.type === 'start') dispatch({ type: 'state', status: 'running' });
        if (command.type === 'pause') dispatch({ type: 'state', status: 'paused' });
        if (command.type === 'resume') dispatch({ type: 'state', status: 'running' });
        if (command.type === 'cancel') {
          dispatch({
            type: 'complete',
            status: 'cancelled',
            metrics: { bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 10, itemsPerSecond: 0, currentPath: '' },
            categories: [],
            topItems: [],
          });
        }
      }
      terminate(): void {}
    }
    Object.defineProperty(window, 'Worker', { configurable: true, value: ControlledWorker });
  });
  await page.reload();
  const directory = testInfo.outputPath('controlled-folder');
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'entry.bin'), Buffer.alloc(32, 1));
  await selectDirectory(page, directory);
  const pause = page.getByRole('button', { name: /Pause|توقف/ });
  await pause.click();
  await expect(page.getByText(/Paused|متوقف‌شده/)).toBeVisible();
  await page.getByRole('button', { name: /Resume|ادامه/ }).click();
  await expect(page.getByText(/Scanning locally|در حال اسکن محلی/)).toBeVisible();
  await page.getByRole('button', { name: /Cancel|لغو/ }).click();
  await expect(page.getByText(/Scan cancelled|اسکن لغو شد/)).toBeVisible();
});

test('shows the exact sanitized AI payload and sends only after explicit consent', async ({ page }, testInfo) => {
  const directory = testInfo.outputPath('private-client-folder');
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'secret-report.pdf'), Buffer.alloc(96, 1));
  await writeFile(join(directory, 'secret-archive.zip'), Buffer.alloc(128, 2));
  await selectDirectory(page, directory);
  await expect(page.getByText(/Scan complete|اسکن کامل شد/)).toBeVisible();

  let requestCount = 0;
  let requestBody = '';
  let authorization = '';
  await page.route('https://openrouter.ai/api/v1/chat/completions', async (route) => {
    requestCount += 1;
    requestBody = route.request().postData() ?? '';
    authorization = await route.request().headerValue('authorization') ?? '';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        model: 'mock/free-model',
        choices: [{
          message: {
            content: JSON.stringify({
              title: 'Private storage review',
              executiveSummary: 'The aggregate scan has low storage pressure.',
              findings: [{
                title: 'Document aggregate',
                evidence: 'One document is represented in the aggregate category.',
                risk: 'low',
                confidence: 0.9,
              }],
              priorities: [{
                title: 'Review future growth',
                reason: 'Current aggregate size is small.',
                confidence: 0.8,
              }],
              reviewTargets: [{
                signal: 'archive',
                disposition: 'archive-candidate',
                rationale: 'Review archive-class items locally before deciding whether to retain them.',
                confidence: 0.86,
              }],
              cautions: ['Metadata cannot determine whether a file is useful.'],
              disclaimer: 'A person must review evidence before any file action.',
              privacyNote: 'Only aggregate metadata was analyzed.',
            }),
          },
        }],
      }),
    });
  });

  await page.getByRole('button', { name: /AI advisor|مشاور هوش مصنوعی/ }).click();
  await page.getByLabel(/Enable AI advisor|فعال‌سازی مشاور هوش مصنوعی/).check();
  await page.getByRole('button', { name: /Prepare exact preview|ساخت پیش‌نمایش دقیق/ }).click();

  const preview = page.getByTestId('advisor-payload');
  await expect(preview).toBeVisible();
  await expect(preview).not.toContainText('secret-report.pdf');
  await expect(preview).not.toContainText('private-client-folder');
  expect(requestCount).toBe(0);

  await page.getByTestId('openrouter-key').fill('sk-or-v1-browser-test-key');
  await page.getByLabel(/I reviewed this exact payload|این payload دقیق را بررسی کردم/).check();
  await page.getByRole('button', { name: /Send for analysis|ارسال برای تحلیل/ }).click();

  await expect(page.getByTestId('advisor-result')).toContainText('Private storage review');
  expect(requestCount).toBe(1);
  expect(authorization).toBe('Bearer sk-or-v1-browser-test-key');
  expect(requestBody).not.toContain('sk-or-v1-browser-test-key');
  expect(requestBody).not.toContain('secret-report.pdf');
  expect(requestBody).not.toContain('private-client-folder');
  expect(requestBody).toContain('"data_collection":"deny"');
  expect(requestBody).toContain('"type":"json_schema"');

  await page.getByRole('button', { name: /Show matching items|نمایش موارد منطبق/ }).click();
  await expect(page.getByTestId('recommendation-filter')).toHaveValue('ai-targeted');
  await expect(page.getByTestId('ai-recommendation-tag')).toBeVisible();
  await expect(page.locator('.virtual-row').filter({ hasText: 'secret-archive.zip' })).toBeVisible();
  await page.locator('.virtual-row').filter({ hasText: 'secret-archive.zip' }).click();
  await expect(page.getByRole('button', { name: /Delete from device|حذف از دستگاه/ })).toBeDisabled();
  await expect(page.getByText(/Fallback and imported scans remain read-only|اسکن fallback و فایل واردشده فقط خواندنی هستند/)).toBeVisible();
});
