import type { ScanItem, ScanSession } from '@/models/scan';
import { forEachSessionItem, getSession, putItemsBatch, putSession } from '@/services/scanDatabase';

const exportFormat = 'storava-web';
const exportVersion = 1;
const importBatchSize = 500;

interface ExportManifest {
  type: 'manifest';
  format: typeof exportFormat;
  version: typeof exportVersion;
  appVersion: string;
  createdAt: string;
  privacy: 'relative-paths-only';
}

interface ExportSessionRecord {
  type: 'session';
  session: ScanSession;
}

interface ExportItemRecord {
  type: 'item';
  item: ScanItem;
}

interface ExportIntegrityRecord {
  type: 'integrity';
  itemCount: number;
  totalBytes: number;
  checksum: string;
}

type ExportRecord = ExportManifest | ExportSessionRecord | ExportItemRecord | ExportIntegrityRecord;

function updateChecksum(current: number, value: string): number {
  let hash = current;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}

function itemSignature(item: ScanItem): string {
  return `${item.relativePath}\u0000${item.size}\u0000${item.modifiedAt ?? ''}`;
}

export async function exportSession(sessionId: string): Promise<{ blob: Blob; fileName: string }> {
  const session = await getSession(sessionId);
  if (!session) throw new Error('Scan session was not found.');
  const chunks: BlobPart[] = [];
  const append = (record: ExportRecord): void => {
    chunks.push(`${JSON.stringify(record)}\n`);
  };
  append({
    type: 'manifest',
    format: exportFormat,
    version: exportVersion,
    appVersion: '0.4.0',
    createdAt: new Date().toISOString(),
    privacy: 'relative-paths-only',
  });
  append({ type: 'session', session: { ...session, source: 'import' } });

  let itemCount = 0;
  let totalBytes = 0;
  let checksum = 2166136261;
  await forEachSessionItem(sessionId, (item) => {
    const exportedItem = { ...item, id: '', sessionId: '' };
    append({ type: 'item', item: exportedItem });
    itemCount += 1;
    totalBytes += item.kind === 'file' ? item.size : 0;
    checksum = updateChecksum(checksum, itemSignature(item));
  });
  append({ type: 'integrity', itemCount, totalBytes, checksum: checksum.toString(16).padStart(8, '0') });
  const safeName = session.rootName.replace(/[^\p{L}\p{N}._-]+/gu, '-').slice(0, 80) || 'scan';
  return {
    blob: new Blob(chunks, { type: 'application/x-ndjson;charset=utf-8' }),
    fileName: `${safeName}-${new Date(session.createdAt).toISOString().slice(0, 10)}.storava-web`,
  };
}

export function downloadExport(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1_000);
}

function validateManifest(record: unknown): asserts record is ExportManifest {
  if (
    typeof record !== 'object'
    || record === null
    || !('type' in record)
    || record.type !== 'manifest'
    || !('format' in record)
    || record.format !== exportFormat
    || !('version' in record)
    || record.version !== exportVersion
  ) {
    throw new Error('Unsupported or corrupt Storava export.');
  }
}

function validateItem(item: unknown): asserts item is ScanItem {
  if (
    typeof item !== 'object'
    || item === null
    || !('relativePath' in item)
    || typeof item.relativePath !== 'string'
    || item.relativePath.startsWith('/')
    || /^[a-zA-Z]:[\\/]/.test(item.relativePath)
    || !('name' in item)
    || typeof item.name !== 'string'
    || !('size' in item)
    || typeof item.size !== 'number'
    || !Number.isFinite(item.size)
    || item.size < 0
  ) {
    throw new Error('The export contains an invalid or absolute file path.');
  }
}

export async function importSession(
  file: File,
  onProgress?: (processedBytes: number, totalBytes: number) => void,
): Promise<ScanSession> {
  if (file.size > 2 * 1024 * 1024 * 1024) throw new Error('Import is larger than the supported 2 GB limit.');
  const reader = file.stream().getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let processedBytes = 0;
  let lineNumber = 0;
  let manifestSeen = false;
  let sourceSession: ScanSession | null = null;
  let integrity: ExportIntegrityRecord | null = null;
  const importedId = crypto.randomUUID();
  let batch: ScanItem[] = [];
  let itemCount = 0;
  let totalBytes = 0;
  let checksum = 2166136261;

  const processLine = async (line: string): Promise<void> => {
    if (!line.trim()) return;
    lineNumber += 1;
    let record: ExportRecord;
    try {
      record = JSON.parse(line) as ExportRecord;
    } catch {
      throw new Error(`Invalid JSON at line ${lineNumber}.`);
    }
    if (!manifestSeen) {
      validateManifest(record);
      manifestSeen = true;
      return;
    }
    if (record.type === 'session') {
      sourceSession = record.session;
      return;
    }
    if (record.type === 'item') {
      validateItem(record.item);
      const item = {
        ...record.item,
        sessionId: importedId,
        id: `${importedId}:${record.item.relativePath || '.'}`,
      };
      batch.push(item);
      itemCount += 1;
      totalBytes += item.kind === 'file' ? item.size : 0;
      checksum = updateChecksum(checksum, itemSignature(item));
      if (batch.length >= importBatchSize) {
        await putItemsBatch(batch);
        batch = [];
      }
      return;
    }
    if (record.type === 'integrity') integrity = record;
  };

  while (true) {
    const { value, done } = await reader.read();
    if (value) {
      processedBytes += value.byteLength;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() ?? '';
      for (const line of lines) await processLine(line);
      onProgress?.(processedBytes, file.size);
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
    }
    if (done) break;
  }
  buffer += decoder.decode();
  if (buffer) await processLine(buffer);
  if (batch.length > 0) await putItemsBatch(batch);
  const finalSource = sourceSession as unknown as ScanSession | null;
  const finalIntegrity = integrity as unknown as ExportIntegrityRecord | null;
  if (!manifestSeen || !finalSource || !finalIntegrity) throw new Error('Export is incomplete.');
  if (
    finalIntegrity.itemCount !== itemCount
    || finalIntegrity.totalBytes !== totalBytes
    || finalIntegrity.checksum !== checksum.toString(16).padStart(8, '0')
  ) {
    throw new Error('Export integrity check failed.');
  }

  const now = Date.now();
  const importedTopItems = finalSource.topItems.map((item) => ({
    ...item,
    sessionId: importedId,
    id: `${importedId}:${item.relativePath || '.'}`,
  }));
  const session: ScanSession = {
    ...finalSource,
    id: importedId,
    source: 'import',
    status: 'imported',
    createdAt: now,
    updatedAt: now,
    completedAt: now,
    metrics: { ...finalSource.metrics, bytes: totalBytes, files: finalSource.metrics.files },
    topItems: importedTopItems,
    schemaVersion: 1,
  };
  await putSession(session);
  return session;
}
