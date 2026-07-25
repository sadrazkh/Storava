/// <reference lib="webworker" />

import type { CategoryAggregate, ScanItem, ScanMetrics, WorkerCommand, WorkerEvent } from '@/models/scan';
import { categorize, extensionOf } from '@/services/categoryService';
import { evaluateRules } from '@/rules/ruleEngine';

const worker = self as unknown as DedicatedWorkerGlobalScope;
const batchSize = 200;
const updateIntervalMs = 160;

let paused = false;
let cancelled = false;
let resumeWaiters: Array<() => void> = [];
let startedAt = 0;
let lastUpdateAt = 0;
let metrics: ScanMetrics;
let pending: ScanItem[] = [];
let categoryTotals = new Map<string, { bytes: number; count: number }>();
let topItems: ScanItem[] = [];
let folders = new Map<string, ScanItem>();

function reset(): void {
  paused = false;
  cancelled = false;
  resumeWaiters = [];
  startedAt = performance.now();
  lastUpdateAt = 0;
  pending = [];
  categoryTotals = new Map();
  topItems = [];
  folders = new Map();
  metrics = { bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 0, itemsPerSecond: 0, currentPath: '' };
}

function categories(): CategoryAggregate[] {
  return [...categoryTotals.entries()]
    .map(([category, aggregate]) => ({ category, ...aggregate }))
    .sort((left, right) => right.bytes - left.bytes);
}

function snapshotMetrics(): ScanMetrics {
  const elapsedMs = Math.max(1, performance.now() - startedAt);
  return {
    ...metrics,
    elapsedMs: Math.round(elapsedMs),
    itemsPerSecond: Math.round(((metrics.files + metrics.folders) * 1000) / elapsedMs),
  };
}

function emit(event: WorkerEvent): void {
  worker.postMessage(event);
}

function updateTopItems(item: ScanItem): void {
  if (item.kind !== 'file') return;
  topItems.push(item);
  topItems.sort((left, right) => right.size - left.size);
  if (topItems.length > 40) topItems.length = 40;
}

function addCategory(item: ScanItem): void {
  if (item.kind !== 'file') return;
  const aggregate = categoryTotals.get(item.category) ?? { bytes: 0, count: 0 };
  aggregate.bytes += item.size;
  aggregate.count += 1;
  categoryTotals.set(item.category, aggregate);
}

function updateFolderSizes(path: string, size: number): void {
  const parts = path.split('/').filter(Boolean);
  parts.pop();
  let current = '';
  for (const part of parts) {
    current = current ? `${current}/${part}` : part;
    const folder = folders.get(current);
    if (folder) folder.size += size;
  }
  const root = folders.get('');
  if (root) root.size += size;
}

async function flush(force = false): Promise<void> {
  const now = performance.now();
  if (!force && pending.length < batchSize && now - lastUpdateAt < updateIntervalMs) return;
  if (pending.length === 0 && !force) return;
  const items = pending;
  pending = [];
  lastUpdateAt = now;
  emit({ type: 'batch', items, metrics: snapshotMetrics(), categories: categories(), topItems: [...topItems] });
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
}

async function waitWhenPaused(): Promise<void> {
  if (!paused || cancelled) return;
  await new Promise<void>((resolve) => resumeWaiters.push(resolve));
}

function createItem(
  sessionId: string,
  relativePath: string,
  name: string,
  kind: 'file' | 'folder',
  size: number,
  modifiedAt: number | null,
): ScanItem {
  const extension = kind === 'file' ? extensionOf(name) : '';
  const parts = relativePath.split('/').filter(Boolean);
  const base = {
    id: `${sessionId}:${relativePath || '.'}`,
    sessionId,
    parentPath: parts.slice(0, -1).join('/'),
    relativePath,
    name,
    kind,
    size,
    modifiedAt,
    extension,
    category: categorize(name, kind),
    depth: Math.max(0, parts.length - 1),
  };
  return { ...base, ...evaluateRules(base) };
}

function queueItem(item: ScanItem): void {
  pending.push(item);
  metrics.currentPath = item.relativePath;
  if (item.kind === 'file') {
    metrics.files += 1;
    metrics.bytes += item.size;
    addCategory(item);
    updateTopItems(item);
    updateFolderSizes(item.relativePath, item.size);
  } else {
    metrics.folders += 1;
    folders.set(item.relativePath, item);
  }
}

function recordError(path: string, error: unknown): void {
  metrics.errors += 1;
  emit({
    type: 'error',
    error: {
      path,
      name: error instanceof DOMException ? error.name : 'ReadError',
      message: error instanceof Error ? error.message : 'Folder entry could not be read.',
    },
    metrics: snapshotMetrics(),
  });
}

async function scanNative(sessionId: string, root: FileSystemDirectoryHandle): Promise<void> {
  queueItem(createItem(sessionId, '', root.name, 'folder', 0, null));
  const stack: Array<{ handle: FileSystemDirectoryHandle; path: string }> = [{ handle: root, path: '' }];
  while (stack.length > 0 && !cancelled) {
    await waitWhenPaused();
    const current = stack.pop();
    if (!current || cancelled) break;
    try {
      for await (const entry of current.handle.values()) {
        await waitWhenPaused();
        if (cancelled) break;
        const relativePath = current.path ? `${current.path}/${entry.name}` : entry.name;
        try {
          if (entry.kind === 'directory') {
            queueItem(createItem(sessionId, relativePath, entry.name, 'folder', 0, null));
            stack.push({ handle: entry, path: relativePath });
          } else {
            const file = await entry.getFile();
            queueItem(createItem(sessionId, relativePath, file.name, 'file', file.size, file.lastModified));
          }
          await flush();
        } catch (error) {
          recordError(relativePath, error);
        }
      }
    } catch (error) {
      recordError(current.path || root.name, error);
    }
  }
}

async function scanFallback(sessionId: string, rootName: string, files: File[]): Promise<void> {
  queueItem(createItem(sessionId, '', rootName, 'folder', 0, null));
  const knownFolders = new Set<string>();
  for (const file of files) {
    await waitWhenPaused();
    if (cancelled) break;
    const parts = (file.webkitRelativePath || file.name).split('/').filter(Boolean);
    if (parts[0] === rootName) parts.shift();
    const fileName = parts.pop() || file.name;
    let folderPath = '';
    for (const part of parts) {
      folderPath = folderPath ? `${folderPath}/${part}` : part;
      if (!knownFolders.has(folderPath)) {
        knownFolders.add(folderPath);
        queueItem(createItem(sessionId, folderPath, part, 'folder', 0, null));
      }
    }
    const relativePath = folderPath ? `${folderPath}/${fileName}` : fileName;
    queueItem(createItem(sessionId, relativePath, fileName, 'file', file.size, file.lastModified));
    await flush();
  }
}

async function start(command: Extract<WorkerCommand, { type: 'start' }>): Promise<void> {
  reset();
  emit({ type: 'state', status: 'running' });
  try {
    if (command.selection.method === 'native') await scanNative(command.sessionId, command.selection.handle);
    else await scanFallback(command.sessionId, command.selection.name, command.selection.files);
    await flush(true);
    const folderUpdates = [...folders.values()];
    for (let index = 0; index < folderUpdates.length; index += batchSize) {
      pending = folderUpdates.slice(index, index + batchSize);
      await flush(true);
    }
    const status = cancelled ? 'cancelled' : 'completed';
    emit({ type: 'complete', status, metrics: snapshotMetrics(), categories: categories(), topItems: [...topItems] });
  } catch (error) {
    recordError(metrics.currentPath, error);
    emit({ type: 'state', status: 'failed' });
  }
}

worker.addEventListener('message', (event: MessageEvent<WorkerCommand>) => {
  const command = event.data;
  if (command.type === 'start') void start(command);
  else if (command.type === 'pause') {
    paused = true;
    emit({ type: 'state', status: 'paused' });
  } else if (command.type === 'resume') {
    paused = false;
    for (const resolve of resumeWaiters.splice(0)) resolve();
    emit({ type: 'state', status: 'running' });
  } else if (command.type === 'cancel') {
    cancelled = true;
    paused = false;
    for (const resolve of resumeWaiters.splice(0)) resolve();
  }
});

export {};
