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
