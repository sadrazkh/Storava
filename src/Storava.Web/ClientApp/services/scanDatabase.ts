import type { ItemPage, ScanFilters, ScanItem, ScanSession } from '@/models/scan';

const databaseName = 'storava-web';
const databaseVersion = 1;
const sessionStore = 'scanSessions';
const itemStore = 'scanItems';

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
      const sessions = database.createObjectStore(sessionStore, { keyPath: 'id' });
      sessions.createIndex('createdAt', 'createdAt');
      const items = database.createObjectStore(itemStore, { keyPath: 'id' });
      items.createIndex('sessionId', 'sessionId');
      items.createIndex('sessionSize', ['sessionId', 'size']);
      items.createIndex('sessionPath', ['sessionId', 'relativePath']);
      items.createIndex('sessionModified', ['sessionId', 'modifiedAt']);
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
  return (!query || item.name.toLocaleLowerCase().includes(query) || item.relativePath.toLocaleLowerCase().includes(query))
    && (filters.category === 'all' || item.category === filters.category)
    && (filters.kind === 'all' || item.kind === filters.kind)
    && (filters.risk === 'all' || item.risk === filters.risk)
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

export async function deleteSession(id: string): Promise<void> {
  const database = await openScanDatabase();
  const transaction = database.transaction([sessionStore, itemStore], 'readwrite');
  transaction.objectStore(sessionStore).delete(id);
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
  const transaction = database.transaction([sessionStore, itemStore], 'readwrite');
  transaction.objectStore(sessionStore).clear();
  transaction.objectStore(itemStore).clear();
  await transactionDone(transaction);
}
