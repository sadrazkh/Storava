import type { FolderSelection } from '@/models/capabilities';
import type { CategoryAggregate, ScanItem, ScanMetrics, ScanSession, ScanStatus, WorkerEvent } from '@/models/scan';
import { applyScanRetention, putDirectoryHandle, putItemsBatch, putSession } from '@/services/scanDatabase';

export interface ScanCallbacks {
  onSession(session: ScanSession): void;
  onBatch(items: ScanItem[], session: ScanSession): void;
  onError(message: string): void;

  /** Called when older scans were discarded, so the history on screen can catch up. */
  onRetention?(discardedIds: string[]): void;

  /**
   * How many scans to keep, read at the moment a scan finishes rather than when this was built.
   *
   * A function and not a number, so changing the setting takes effect on the next scan without
   * anything having to remember to tell the scanner. Absent means the default.
   */
  keepScans?(): number;
}

export class ScannerService {
  private worker: Worker | null = null;
  private session: ScanSession | null = null;
  private writeChain = Promise.resolve();

  constructor(private readonly callbacks: ScanCallbacks) {}

  async start(selection: FolderSelection): Promise<ScanSession> {
    this.dispose();
    const now = Date.now();
    this.session = {
      id: crypto.randomUUID(),
      rootName: selection.name,
      source: selection.method,
      status: 'running',
      createdAt: now,
      updatedAt: now,
      completedAt: null,
      metrics: { bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 0, itemsPerSecond: 0, currentPath: '' },
      categories: [],
      topItems: [],
      schemaVersion: 1,
    };
    await putSession(this.session);
    if (selection.method === 'native') {
      try {
        await putDirectoryHandle(this.session.id, selection.handle);
      } catch {
        this.callbacks.onError('The browser could not persist this folder permission; scan results remain available.');
      }
    }
    this.callbacks.onSession({ ...this.session });
    this.worker = new Worker(new URL('../workers/scan.worker.ts', import.meta.url), { type: 'module' });
    this.worker.addEventListener('message', (event: MessageEvent<WorkerEvent>) => this.handleEvent(event.data));
    this.worker.addEventListener('error', (event) => {
      this.updateSession('failed');
      this.callbacks.onError(event.message);
    });
    this.worker.postMessage({ type: 'start', sessionId: this.session.id, selection });
    return this.session;
  }

  pause(): void {
    if (!this.worker) return;
    this.updateSession('paused');
    this.worker.postMessage({ type: 'pause' });
  }

  resume(): void {
    if (!this.worker) return;
    this.updateSession('running');
    this.worker.postMessage({ type: 'resume' });
  }
  cancel(): void { this.worker?.postMessage({ type: 'cancel' }); }

  dispose(): void {
    this.worker?.terminate();
    this.worker = null;
  }

  private handleEvent(event: WorkerEvent): void {
    if (!this.session) return;
    if (event.type === 'batch') {
      this.applyAggregates(event.metrics, event.categories, event.topItems);
      const session = structuredClone(this.session);
      this.writeChain = this.writeChain
        .then(() => putItemsBatch(event.items))
        .then(() => putSession(session))
        .catch((error: unknown) => this.callbacks.onError(error instanceof Error ? error.message : 'Local storage failed.'));
      this.callbacks.onBatch(event.items, structuredClone(this.session));
    } else if (event.type === 'state') {
      this.updateSession(event.status);
    } else if (event.type === 'error') {
      this.session.metrics = event.metrics;
      this.callbacks.onError(`${event.error.path}: ${event.error.message}`);
    } else {
      this.applyAggregates(event.metrics, event.categories, event.topItems);
      this.session.status = event.status;
      this.session.completedAt = Date.now();
      this.session.updatedAt = Date.now();
      const completed = structuredClone(this.session);
      this.writeChain = this.writeChain
        .then(() => putSession(completed))
        // Older scans go now that a newer one exists. Chained after the write rather than awaited
        // by the caller: the scan is finished and saved by this point, and nothing on screen is
        // waiting for the tidying up. The scan just taken is named so it can never be the one
        // discarded, however the clock behaved.
        .then(() => applyScanRetention(this.callbacks.keepScans?.() ?? 3, completed.id))
        .then((discarded) => {
          if (discarded.length > 0) this.callbacks.onRetention?.(discarded);
        })
        .catch(() => {
          // Housekeeping must not be able to turn a finished scan into a failure.
        });
      this.callbacks.onSession(completed);
      this.worker?.terminate();
      this.worker = null;
    }
  }

  private applyAggregates(metrics: ScanMetrics, categories: CategoryAggregate[], topItems: ScanItem[]): void {
    if (!this.session) return;
    this.session.metrics = metrics;
    this.session.categories = categories;
    this.session.topItems = topItems;
    this.session.updatedAt = Date.now();
  }

  private updateSession(status: ScanStatus): void {
    if (!this.session) return;
    this.session.status = status;
    this.session.updatedAt = Date.now();
    const snapshot = structuredClone(this.session);
    void putSession(snapshot);
    this.callbacks.onSession(snapshot);
  }
}
