import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AdvisorResult } from '@/models/advisor';
import type { ScanItem, ScanSession } from '@/models/scan';
import {
  clearAllLocalData,
  getAdvisorResult,
  listSessions,
  putAdvisorResult,
  putItemsBatch,
  putSession,
} from '@/services/scanDatabase';
import { clearWebStorage, describeWebStorage, hasStoredApiKey } from '@/services/webStorageReport';

function session(id: string): ScanSession {
  return {
    id,
    rootName: 'root',
    source: 'fallback',
    status: 'completed',
    createdAt: 1,
    updatedAt: 2,
    completedAt: 2,
    metrics: { bytes: 10, files: 1, folders: 0, errors: 0, elapsedMs: 5, itemsPerSecond: 200, currentPath: '' },
    categories: [],
    topItems: [],
    schemaVersion: 1,
  };
}

function item(sessionId: string, name: string): ScanItem {
  return {
    id: `${sessionId}:${name}`,
    sessionId,
    parentPath: '',
    relativePath: name,
    name,
    kind: 'file',
    size: 10,
    modifiedAt: 1,
    extension: 'bin',
    category: 'other',
    depth: 0,
    ruleIds: [],
    risk: 'none',
  };
}

function advice(): AdvisorResult {
  return {
    title: 'advice',
    executiveSummary: 'summary',
    findings: [],
    priorities: [],
    reviewTargets: [],
    itemTargets: [],
    cautions: [],
    disclaimer: '',
    privacyNote: '',
    model: 'test/model',
    generatedAt: '2026-01-01T00:00:00.000Z',
  };
}

async function entryFor(kind: 'scans' | 'advice' | 'settings' | 'apiKey') {
  const report = await describeWebStorage();
  const found = report.entries.find((candidate) => candidate.kind === kind);
  expect(found).toBeDefined();
  return found!;
}

describe('what this edition is using in the browser', () => {
  beforeEach(async () => {
    await clearAllLocalData();
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('counts records rather than claiming a size it cannot measure', async () => {
    await putSession(session('a'));
    await putItemsBatch([item('a', 'one.bin'), item('a', 'two.bin')]);

    const scans = await entryFor('scans');

    // One session plus two items. IndexedDB exposes no per-store size, so a byte figure here would
    // have to be invented — the type does not have one.
    expect(scans.records).toBe(3);
    expect(scans).not.toHaveProperty('bytes');
  });

  it('reports the browsers own total for the origin', async () => {
    vi.stubGlobal('navigator', {
      ...navigator,
      storage: { estimate: () => Promise.resolve({ usage: 4096, quota: 1024 * 1024 }) },
    });

    const report = await describeWebStorage();

    expect(report.usedBytes).toBe(4096);
    expect(report.quotaBytes).toBe(1024 * 1024);
  });

  /** Zero would read as "nothing stored", which is a different claim from "the browser did not say". */
  it('leaves the total unset when the browser will not answer', async () => {
    vi.stubGlobal('navigator', {
      ...navigator,
      storage: { estimate: () => Promise.reject(new Error('not in a private window')) },
    });

    const report = await describeWebStorage();

    expect(report.usedBytes).toBeUndefined();
  });

  /**
   * Some browsers report what is used without saying what is allowed. Falling back to zero there
   * would put "of 0 B the browser allows" on screen, which is both false and alarming.
   */
  it('leaves the allowance unset when only the usage is reported', async () => {
    vi.stubGlobal('navigator', {
      ...navigator,
      storage: { estimate: () => Promise.resolve({ usage: 4096 }) },
    });

    const report = await describeWebStorage();

    expect(report.usedBytes).toBe(4096);
    expect(report.quotaBytes).toBeUndefined();
  });

  it('leaves the total unset where the browser has no estimate at all', async () => {
    vi.stubGlobal('navigator', { ...navigator, storage: undefined });

    const report = await describeWebStorage();

    expect(report.usedBytes).toBeUndefined();
    expect((await describeWebStorage()).entries.length).toBeGreaterThan(0);
  });

  it('notices a stored key without reading it out', async () => {
    expect(hasStoredApiKey()).toBe(false);

    localStorage.setItem('storava.advisor.settings.v1', JSON.stringify({ apiKey: 'sk-secret' }));

    expect(hasStoredApiKey()).toBe(true);
    expect((await entryFor('apiKey')).records).toBe(1);
  });

  it('treats settings with no key as no key', () => {
    localStorage.setItem('storava.advisor.settings.v1', JSON.stringify({ model: 'free' }));

    expect(hasStoredApiKey()).toBe(false);
  });

  /**
   * An empty string is what the advisor panel leaves behind when somebody clears the field, so this
   * is the ordinary case, not a strange one. Counting it as a stored key would tell a person their
   * key is safe here when there is nothing to lose.
   */
  it('treats an emptied key as no key', () => {
    localStorage.setItem('storava.advisor.settings.v1', JSON.stringify({ apiKey: '' }));

    expect(hasStoredApiKey()).toBe(false);
  });

  it('survives settings that are not valid json', () => {
    localStorage.setItem('storava.advisor.settings.v1', 'not json at all');

    expect(hasStoredApiKey()).toBe(false);
  });

  it('will not empty the stored key from here', async () => {
    localStorage.setItem('storava.advisor.settings.v1', JSON.stringify({ apiKey: 'sk-secret' }));

    const result = await clearWebStorage('apiKey');

    expect(result.records).toBe(0);
    expect(hasStoredApiKey()).toBe(true);
  });

  it('empties the scans and leaves the advice alone', async () => {
    await putSession(session('a'));
    await putItemsBatch([item('a', 'one.bin')]);
    await putAdvisorResult('a', advice());

    const result = await clearWebStorage('scans');

    expect(result.records).toBe(2);
    expect(await listSessions()).toEqual([]);

    // The advice cost a paid request and is a separate row with its own button. Taking it along
    // would empty a store the user did not ask about.
    expect(await getAdvisorResult('a')).toBeDefined();
  });

  it('empties the advice and leaves the scans alone', async () => {
    await putSession(session('a'));
    await putAdvisorResult('a', advice());

    const result = await clearWebStorage('advice');

    expect(result.records).toBe(1);
    expect(await getAdvisorResult('a')).toBeUndefined();
    expect((await listSessions()).length).toBe(1);
  });

  it('empties the preferences without touching the scans', async () => {
    await putSession(session('a'));
    localStorage.setItem('storava.locale', 'fa-IR');
    localStorage.setItem('storava.keepScans', '5');

    const result = await clearWebStorage('settings');

    expect(result.records).toBe(2);
    expect(localStorage.getItem('storava.locale')).toBeNull();
    expect(localStorage.getItem('storava.keepScans')).toBeNull();
    expect((await listSessions()).length).toBe(1);
  });

  /**
   * Clearing the preferences must not take the API key with it. They live in the same storage and
   * only the key cannot be recreated, so this is the one pairing worth pinning down.
   */
  it('keeps the api key when the preferences go', async () => {
    localStorage.setItem('storava.locale', 'fa-IR');
    localStorage.setItem('storava.advisor.settings.v1', JSON.stringify({ apiKey: 'sk-secret' }));

    await clearWebStorage('settings');

    expect(hasStoredApiKey()).toBe(true);
  });

  it('reports nothing to empty when a store is already empty', async () => {
    expect((await clearWebStorage('advice')).records).toBe(0);
  });
});
