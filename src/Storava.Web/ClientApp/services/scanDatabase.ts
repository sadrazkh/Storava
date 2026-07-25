import type { AdvisorResult, StoredAdvisorResult } from '@/models/advisor';
import type { ItemPage, ItemRemovalResult, ScanFilters, ScanItem, ScanSession } from '@/models/scan';

const databaseName = 'storava-web';
const databaseVersion = 2;
const sessionStore = 'scanSessions';
const itemStore = 'scanItems';
const directoryHandleStore = 'directoryHandles';
const advisorResultStore = 'advisorResults';

let databasePromise: Promise<IDBDatabase> | null = null;

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed.'));
  });
}

function transactionDone(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error('IndexedDB transaction failed.'));
    transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB transaction aborted.'));
  });
}

export function openScanDatabase(): Promise<IDBDatabase> {
  if (databasePromise) return databasePromise;
  databasePromise = new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, databaseVersion);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(sessionStore)) {
        const sessions = database.createObjectStore(sessionStore, { keyPath: 'id' });
        sessions.createIndex('createdAt', 'createdAt');
      }
      if (!database.objectStoreNames.contains(itemStore)) {
        const items = database.createObjectStore(itemStore, { keyPath: 'id' });
        items.createIndex('sessionId', 'sessionId');
        items.createIndex('sessionSize', ['sessionId', 'size']);
        items.createIndex('sessionPath', ['sessionId', 'relativePath']);
        items.createIndex('sessionModified', ['sessionId', 'modifiedAt']);
      }
      if (!database.objectStoreNames.contains(directoryHandleStore)) {
        database.createObjectStore(directoryHandleStore, { keyPath: 'sessionId' });
      }
      if (!database.objectStoreNames.contains(advisorResultStore)) {
        database.createObjectStore(advisorResultStore, { keyPath: 'sessionId' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Could not open local scan database.'));
  });
  return databasePromise;
}

export async function putSession(session: ScanSession): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(sessionStore, 'readwrite');
  transaction.objectStore(sessionStore).put(session);
  await transactionDone(transaction);
}

export async function getSession(id: string): Promise<ScanSession | undefined> {
  const database = await openScanDatabase();
  const transaction = database.transaction(sessionStore, 'readonly');
  return requestResult(transaction.objectStore(sessionStore).get(id)) as Promise<ScanSession | undefined>;
}

export async function listSessions(): Promise<ScanSession[]> {
  const database = await openScanDatabase();
  const transaction = database.transaction(sessionStore, 'readonly');
  const sessions = await requestResult(transaction.objectStore(sessionStore).getAll()) as ScanSession[];
  return sessions.sort((left, right) => right.createdAt - left.createdAt);
}

export async function putItemsBatch(items: ScanItem[]): Promise<void> {
  if (items.length === 0) return;
  const database = await openScanDatabase();
  const transaction = database.transaction(itemStore, 'readwrite');
  const store = transaction.objectStore(itemStore);
  for (const item of items) store.put(item);
  await transactionDone(transaction);
}

function itemMatches(item: ScanItem, filters: ScanFilters): boolean {
  const query = filters.query.trim().toLocaleLowerCase();
  const matchesRecommendation = filters.recommendation === 'all'
    || (filters.recommendation === 'local-signals' && item.ruleIds.length > 0)
    || (filters.recommendation === 'ai-targeted' && item.ruleIds.some((rule) => filters.aiRuleIds.includes(rule)));
  return (!query || item.name.toLocaleLowerCase().includes(query) || item.relativePath.toLocaleLowerCase().includes(query))
    && (filters.category === 'all' || item.category === filters.category)
    && (filters.kind === 'all' || item.kind === filters.kind)
    && (filters.risk === 'all' || item.risk === filters.risk)
    && matchesRecommendation
    && (filters.parentPath === null || item.parentPath === filters.parentPath);
}

export async function queryItems(
  sessionId: string,
  filters: ScanFilters,
  offset = 0,
  limit = 1000,
): Promise<ItemPage> {
  const database = await openScanDatabase();
  const transaction = database.transaction(itemStore, 'readonly');
  const store = transaction.objectStore(itemStore);
  const indexName = filters.sort.startsWith('size') ? 'sessionSize'
    : filters.sort === 'modified' ? 'sessionModified'
      : 'sessionPath';
  const index = store.index(indexName);
  const range = IDBKeyRange.bound([sessionId, Number.NEGATIVE_INFINITY], [sessionId, []]);
  const direction: IDBCursorDirection = filters.sort === 'size-desc' || filters.sort === 'modified' ? 'prev' : 'next';
  const items: ScanItem[] = [];
  let skipped = 0;
  let hasMore = false;

  await new Promise<void>((resolve, reject) => {
    const request = index.openCursor(range, direction);
    request.onerror = () => reject(request.error ?? new Error('Could not query scan items.'));
    request.onsuccess = () => {
      const cursor = request.result;
      if (!cursor) {
        resolve();
        return;
      }
      const item = cursor.value as ScanItem;
      if (item.sessionId === sessionId && itemMatches(item, filters)) {
        if (skipped < offset) skipped += 1;
        else if (items.length < limit) items.push(item);
        else {
          hasMore = true;
          resolve();
          return;
        }
      }
      cursor.continue();
    };
  });

  if (filters.sort === 'name') {
    items.sort((left, right) => left.name.localeCompare(right.name));
  } else if (filters.sort === 'size-asc') {
    items.sort((left, right) => left.size - right.size);
  }
  return { items, hasMore };
}

export async function forEachSessionItem(
  sessionId: string,
  callback: (item: ScanItem) => void | Promise<void>,
): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(itemStore, 'readonly');
  const index = transaction.objectStore(itemStore).index('sessionId');
  await new Promise<void>((resolve, reject) => {
    const request = index.openCursor(IDBKeyRange.only(sessionId));
    request.onerror = () => reject(request.error ?? new Error('Could not read scan items.'));
    request.onsuccess = async () => {
      const cursor = request.result;
      if (!cursor) {
        resolve();
        return;
      }
      try {
        await callback(cursor.value as ScanItem);
        cursor.continue();
      } catch (error) {
        reject(error instanceof Error ? error : new Error('Could not enumerate scan items.'));
      }
    };
  });
}

export async function putDirectoryHandle(sessionId: string, handle: FileSystemDirectoryHandle): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(directoryHandleStore, 'readwrite');
  transaction.objectStore(directoryHandleStore).put({ sessionId, handle });
  await transactionDone(transaction);
}

export async function getDirectoryHandle(sessionId: string): Promise<FileSystemDirectoryHandle | undefined> {
  const database = await openScanDatabase();
  const transaction = database.transaction(directoryHandleStore, 'readonly');
  const record = await requestResult(transaction.objectStore(directoryHandleStore).get(sessionId)) as
    { sessionId: string; handle: FileSystemDirectoryHandle } | undefined;
  return record?.handle;
}

export async function putAdvisorResult(sessionId: string, result: AdvisorResult): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(advisorResultStore, 'readwrite');
  const record: StoredAdvisorResult = { sessionId, result };
  transaction.objectStore(advisorResultStore).put(record);
  await transactionDone(transaction);
}

export async function getAdvisorResult(sessionId: string): Promise<AdvisorResult | undefined> {
  const database = await openScanDatabase();
  const transaction = database.transaction(advisorResultStore, 'readonly');
  const record = await requestResult(transaction.objectStore(advisorResultStore).get(sessionId)) as
    StoredAdvisorResult | undefined;
  return record?.result;
}

export async function removeItemTree(sessionId: string, relativePath: string): Promise<ItemRemovalResult> {
  const database = await openScanDatabase();
  const transaction = database.transaction(itemStore, 'readwrite');
  const store = transaction.objectStore(itemStore);
  const index = store.index('sessionId');
  let deletedFiles = 0;
  let deletedFolders = 0;
  let freedBytes = 0;

  await new Promise<void>((resolve, reject) => {
    const request = index.openCursor(IDBKeyRange.only(sessionId));
    request.onerror = () => reject(request.error ?? new Error('Could not update deleted scan items.'));
    request.onsuccess = () => {
      const cursor = request.result;
      if (!cursor) {
        resolve();
        return;
      }
      const item = cursor.value as ScanItem;
      if (item.relativePath === relativePath || item.relativePath.startsWith(`${relativePath}/`)) {
        if (item.kind === 'file') {
          deletedFiles += 1;
          freedBytes += item.size;
        } else {
          deletedFolders += 1;
        }
        cursor.delete();
      }
      cursor.continue();
    };
  });
  await transactionDone(transaction);

  const ancestors = new Set<string>(['']);
  const pathParts = relativePath.split('/').filter(Boolean);
  pathParts.pop();
  let ancestor = '';
  for (const part of pathParts) {
    ancestor = ancestor ? `${ancestor}/${part}` : part;
    ancestors.add(ancestor);
  }
  const ancestorTransaction = database.transaction(itemStore, 'readwrite');
  const ancestorIndex = ancestorTransaction.objectStore(itemStore).index('sessionId');
  await new Promise<void>((resolve, reject) => {
    const request = ancestorIndex.openCursor(IDBKeyRange.only(sessionId));
    request.onerror = () => reject(request.error ?? new Error('Could not update folder aggregates.'));
    request.onsuccess = () => {
      const cursor = request.result;
      if (!cursor) {
        resolve();
        return;
      }
      const item = cursor.value as ScanItem;
      if (item.kind === 'folder' && ancestors.has(item.relativePath)) {
        cursor.update({ ...item, size: Math.max(0, item.size - freedBytes) });
      }
      cursor.continue();
    };
  });
  await transactionDone(ancestorTransaction);

  const storedSession = await getSession(sessionId);
  if (!storedSession) throw new Error('Scan session was not found.');
  let files = 0;
  let folders = 0;
  let bytes = 0;
  const categories = new Map<string, { bytes: number; count: number }>();
  const topItems: ScanItem[] = [];
  await forEachSessionItem(sessionId, (item) => {
    if (item.kind === 'folder') {
      folders += 1;
      return;
    }
    files += 1;
    bytes += item.size;
    const aggregate = categories.get(item.category) ?? { bytes: 0, count: 0 };
    aggregate.bytes += item.size;
    aggregate.count += 1;
    categories.set(item.category, aggregate);
    topItems.push(item);
    topItems.sort((left, right) => right.size - left.size);
    if (topItems.length > 40) topItems.length = 40;
  });
  const updatedSession: ScanSession = {
    ...storedSession,
    updatedAt: Date.now(),
    metrics: {
      ...storedSession.metrics,
      bytes,
      files,
      folders,
      currentPath: '',
    },
    categories: [...categories.entries()]
      .map(([category, aggregate]) => ({ category, ...aggregate }))
      .sort((left, right) => right.bytes - left.bytes),
    topItems,
  };
  await putSession(updatedSession);
  return { session: updatedSession, deletedFiles, deletedFolders, freedBytes };
}

export async function deleteSession(id: string): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(
    [sessionStore, itemStore, directoryHandleStore, advisorResultStore],
    'readwrite',
  );
  transaction.objectStore(sessionStore).delete(id);
  transaction.objectStore(directoryHandleStore).delete(id);
  transaction.objectStore(advisorResultStore).delete(id);
  const index = transaction.objectStore(itemStore).index('sessionId');
  const cursorRequest = index.openKeyCursor(IDBKeyRange.only(id));
  cursorRequest.onsuccess = () => {
    const cursor = cursorRequest.result;
    if (!cursor) return;
    transaction.objectStore(itemStore).delete(cursor.primaryKey);
    cursor.continue();
  };
  await transactionDone(transaction);
}

export async function clearAllLocalData(): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction(
    [sessionStore, itemStore, directoryHandleStore, advisorResultStore],
    'readwrite',
  );
  transaction.objectStore(sessionStore).clear();
  transaction.objectStore(itemStore).clear();
  transaction.objectStore(directoryHandleStore).clear();
  transaction.objectStore(advisorResultStore).clear();
  await transactionDone(transaction);
}
