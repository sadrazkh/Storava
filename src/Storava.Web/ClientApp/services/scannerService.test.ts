import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ScanMetrics } from '@/models/scan';
import { ScannerService } from '@/services/scannerService';
import { clearAllLocalData, listSessions, putSession } from '@/services/scanDatabase';

/**
 * A stand-in for the scanning worker.
 *
 * The real one walks a folder through the File System Access API, which no test environment has.
 * What matters here is only what the scanner does when the worker says it has finished, so this
 * exists to let the completion event be delivered at all.
 */
class FakeWorker {
  static latest: FakeWorker | null = null;

  private listeners: Array<(event: MessageEvent) => void> = [];

  constructor() {
    FakeWorker.latest = this;
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void): void {
    if (type === 'message') this.listeners.push(listener);
  }

  postMessage(): void {
    // The scanner starts the walk this way; there is nothing to walk.
  }

  terminate(): void {
    // Called when a scan finishes.
  }

  finish(): void {
    const metrics: ScanMetrics = {
      bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 1, itemsPerSecond: 0, currentPath: '',
    };

    for (const listener of this.listeners) {
      listener({
        data: { type: 'complete', status: 'completed', metrics, categories: [], topItems: [] },
      } as MessageEvent);
    }
  }
}

/**
 * The wire between the setting and the deletion.
 *
 * The count and the discarding are each covered on their own, which leaves the one thing that
 * makes the setting real: that the scanner asks for it. A control that stores a number nothing
 * reads looks exactly like a working one until somebody counts their scans.
 */
describe('what the scanner does when a scan finishes', () => {
  beforeEach(async () => {
    await clearAllLocalData();
    FakeWorker.latest = null;
    vi.stubGlobal('Worker', FakeWorker);
    vi.stubGlobal('crypto', { ...globalThis.crypto, randomUUID: () => `id-${Math.random()}` });
  });

  it('asks how many scans to keep, rather than deciding for itself', async () => {
    const keepScans = vi.fn(() => 5);

    const scanner = new ScannerService({
      onSession: () => {},
      onBatch: () => {},
      onError: () => {},
      keepScans,
    });

    await scanner.start({ method: 'fallback', name: 'test', files: [] } as never);
    FakeWorker.latest!.finish();
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(keepScans).toHaveBeenCalled();
  });

  /** And uses the answer: five kept means five kept, not the default three. */
  it('keeps the number it was given', async () => {
    for (let index = 0; index < 6; index += 1) {
      await putSession({
        id: `old-${index}`,
        rootName: `old-${index}`,
        source: 'native',
        status: 'completed',
        createdAt: index,
        updatedAt: index,
        completedAt: index,
        metrics: {
          bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 0, itemsPerSecond: 0, currentPath: '',
        },
        categories: [],
        topItems: [],
        schemaVersion: 1,
      });
    }

    const scanner = new ScannerService({
      onSession: () => {},
      onBatch: () => {},
      onError: () => {},
      keepScans: () => 5,
    });

    await scanner.start({ method: 'fallback', name: 'fresh', files: [] } as never);
    FakeWorker.latest!.finish();
    await new Promise((resolve) => setTimeout(resolve, 40));

    // Five, because that is what was asked for. Three would mean the setting is decoration.
    expect(await listSessions()).toHaveLength(5);
  });
});
