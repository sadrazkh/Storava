import { beforeEach, describe, expect, it } from 'vitest';
import type { ScanSession, ScanStatus } from '@/models/scan';
import {
  applyScanRetention,
  clearAllLocalData,
  listSessions,
  putItemsBatch,
  putSession,
  queryItems,
} from '@/services/scanDatabase';

/**
 * Retention discards old scans so the browser's storage stops growing without limit.
 *
 * A scan of a large folder is hundreds of thousands of records, and until now nothing ever removed
 * them — the only way to reclaim any of it was for somebody to remember to press delete. The
 * desktop edition grew a six-gigabyte database exactly that way.
 *
 * As on the desktop, what this must *not* discard matters as much as what it does.
 */
describe('scan retention', () => {
  beforeEach(async () => {
    await clearAllLocalData();
  });

  it('keeps only the newest scans', async () => {
    await givenScans(6);

    const discarded = await applyScanRetention(3);

    expect(discarded).toHaveLength(3);
    expect((await listSessions()).map((session) => session.id)).toEqual(['scan-5', 'scan-4', 'scan-3']);
  });

  it('does nothing when there is room to spare', async () => {
    await givenScans(3);

    expect(await applyScanRetention(3)).toEqual([]);
    expect(await listSessions()).toHaveLength(3);
  });

  /** The scan on screen stays whatever its age, or the page empties underneath the user. */
  it('never discards the scan being viewed', async () => {
    await givenScans(6);

    await applyScanRetention(2, 'scan-0');

    const left = (await listSessions()).map((session) => session.id);
    expect(left).toContain('scan-0');
    expect(left).toHaveLength(3); // the two newest, plus the one being viewed
  });

  /** A scan still being written is the one running, not old data to throw away. */
  it('leaves a scan that is still running alone', async () => {
    await givenScans(6);
    await putSession(session('scan-1', 1, 'running'));

    await applyScanRetention(3);

    const left = (await listSessions()).map((session_) => session_.id);
    expect(left).toContain('scan-1');
    expect(left).not.toContain('scan-0');
  });

  /** Keeping none would mean discarding the scan just taken. One is the floor. */
  it('treats keeping none as keeping one', async () => {
    await givenScans(4);

    await applyScanRetention(0);

    expect(await listSessions()).toHaveLength(1);
  });

  /** The items go with the session, which is where nearly all of the size was. */
  it('discards the items of a discarded scan', async () => {
    await givenScans(4);
    await givenItems('scan-0');
    await givenItems('scan-3');

    await applyScanRetention(1);

    expect(await countItems('scan-0')).toBe(0);
    expect(await countItems('scan-3')).toBe(3);
  });
});

// --- setup -------------------------------------------------------------------------------

/** Scans numbered oldest-first, so scan-0 is the first to go. */
async function givenScans(count: number): Promise<void> {
  for (let index = 0; index < count; index += 1) {
    await putSession(session(`scan-${index}`, index));
  }
}

function session(id: string, index: number, status: ScanStatus = 'completed'): ScanSession {
  const createdAt = Date.UTC(2026, 0, 1) + index * 86_400_000;

  return {
    id,
    rootName: id,
    source: 'native',
    status,
    createdAt,
    updatedAt: createdAt,
    completedAt: status === 'completed' ? createdAt + 3_600_000 : null,
    metrics: {
      bytes: 0, files: 0, folders: 0, errors: 0, elapsedMs: 0, itemsPerSecond: 0, currentPath: '',
    },
    categories: [],
    topItems: [],
    schemaVersion: 1,
  };
}

async function givenItems(sessionId: string): Promise<void> {
  await putItemsBatch([0, 1, 2].map((index) => ({
    id: `${sessionId}-item-${index}`,
    sessionId,
    parentPath: `/${sessionId}`,
    relativePath: `${sessionId}/file-${index}.tmp`,
    name: `file-${index}.tmp`,
    extension: '.tmp',
    kind: 'file' as const,
    size: 1024,
    modifiedAt: null,
    depth: 1,
    category: 'other',
    ruleIds: [],
    risk: 'low' as const,
  })));
}

async function countItems(sessionId: string): Promise<number> {
  const page = await queryItems(sessionId, {
    query: '',
    category: 'all',
    kind: 'all',
    risk: 'all',
    recommendation: 'all',
    aiRuleIds: [],
    sort: 'size-desc',
    parentPath: null,
  });

  return page.items.length;
}
