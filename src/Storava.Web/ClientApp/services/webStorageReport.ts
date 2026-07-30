import { openScanDatabase } from '@/services/scanDatabase';

/**
 * What this edition has put in the browser, and which of it can be thrown away.
 *
 * The desktop can stat a file; a page cannot. IndexedDB has no per-store size — there is no API for
 * it and none is coming — so this reports the number of records in each store and one measured total
 * for the whole origin, from `navigator.storage.estimate()`. Apportioning that total across the
 * stores by record count would produce numbers that look authoritative and are invented, so it is
 * not done: counts are counts and the total is the total.
 */
export type WebStorageKind = 'scans' | 'advice' | 'settings' | 'apiKey';

export interface WebStorageEntry {
  kind: WebStorageKind;
  /** Records held, summed across the stores that make up this entry. */
  records: number;
  /** Whether this edition will empty it from the storage panel. */
  canClear: boolean;
}

export interface WebStorageReport {
  entries: WebStorageEntry[];
  /**
   * Bytes this origin occupies, as the browser accounts for it — everything below plus the cached
   * application itself. Undefined where the browser does not answer, which is the honest value:
   * showing zero would read as "nothing stored".
   */
  usedBytes?: number;
  /** What the browser is willing to give this origin, when it says. */
  quotaBytes?: number;
}

export interface WebStorageClearResult {
  /** Records removed. Bytes are deliberately absent: nothing here can measure them. */
  records: number;
}

const scanStores = ['scanSessions', 'scanItems', 'directoryHandles'] as const;
const adviceStores = ['advisorResults', 'recommendations'] as const;

const preferenceKeys = ['storava.locale', 'storava.theme', 'storava.keepScans'] as const;
const advisorSettingsKey = 'storava.advisor.settings.v1';

function countOf(database: IDBDatabase, store: string): Promise<number> {
  if (!database.objectStoreNames.contains(store)) return Promise.resolve(0);

  return new Promise((resolve) => {
    const request = database.transaction(store, 'readonly').objectStore(store).count();
    request.onsuccess = () => resolve(request.result);
    // A store that cannot be counted is reported as empty rather than failing the whole panel.
    request.onerror = () => resolve(0);
  });
}

async function countAll(database: IDBDatabase, stores: readonly string[]): Promise<number> {
  const counts = await Promise.all(stores.map((store) => countOf(database, store)));
  return counts.reduce((total, count) => total + count, 0);
}

function storedPreferenceCount(): number {
  return preferenceKeys.filter((key) => localStorage.getItem(key) !== null).length;
}

/** Whether an API key is being kept, without reading it. */
export function hasStoredApiKey(): boolean {
  try {
    const raw = localStorage.getItem(advisorSettingsKey);
    if (!raw) return false;
    const parsed = JSON.parse(raw) as { apiKey?: unknown };
    return typeof parsed.apiKey === 'string' && parsed.apiKey.length > 0;
  } catch {
    return false;
  }
}

export async function describeWebStorage(): Promise<WebStorageReport> {
  const database = await openScanDatabase();

  const [scans, advice] = await Promise.all([
    countAll(database, scanStores),
    countAll(database, adviceStores),
  ]);

  let usedBytes: number | undefined;
  let quotaBytes: number | undefined;

  // The guard and the catch overlap, deliberately: a browser without the API would land in the
  // catch anyway and produce the same undefined. Asking first is not error handling used as flow
  // control, and both endings are the same one — the browser did not say, so nothing is claimed.
  if (navigator.storage?.estimate) {
    try {
      const estimate = await navigator.storage.estimate();
      usedBytes = estimate.usage;
      quotaBytes = estimate.quota;
    } catch {
      // Some browsers refuse in a private window.
    }
  }

  const entries: WebStorageEntry[] = [
    { kind: 'scans', records: scans, canClear: true },
    { kind: 'advice', records: advice, canClear: true },
    { kind: 'settings', records: storedPreferenceCount(), canClear: true },

    // Listed so it is not a surprise, and not clearable here: it is the one thing in this list that
    // cannot be recreated. It is removed from the advisor panel, beside what losing it means.
    { kind: 'apiKey', records: hasStoredApiKey() ? 1 : 0, canClear: false },
  ];

  return { entries, usedBytes, quotaBytes };
}

/**
 * Empties one store. Refuses anything the report marks unclearable rather than trusting the caller,
 * for the same reason the desktop does: the reasons are reasons, not presentation.
 */
export async function clearWebStorage(kind: WebStorageKind): Promise<WebStorageClearResult> {
  const report = await describeWebStorage();
  const entry = report.entries.find((candidate) => candidate.kind === kind);
  if (!entry?.canClear) return { records: 0 };

  const database = await openScanDatabase();

  if (kind === 'settings') {
    for (const key of preferenceKeys) localStorage.removeItem(key);
    return { records: entry.records };
  }

  const stores = kind === 'scans' ? scanStores : adviceStores;
  const present = stores.filter((store) => database.objectStoreNames.contains(store));
  if (present.length === 0) return { records: 0 };

  const transaction = database.transaction(present, 'readwrite');
  for (const store of present) transaction.objectStore(store).clear();

  await new Promise<void>((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error('Could not empty local storage.'));
    transaction.onabort = () => reject(transaction.error ?? new Error('Emptying local storage was aborted.'));
  });

  return { records: entry.records };
}
