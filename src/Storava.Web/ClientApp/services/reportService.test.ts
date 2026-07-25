import { describe, expect, it } from 'vitest';
import type { ScanSession } from '@/models/scan';
import { createOfflineReport, type ReportCopy } from '@/services/reportService';

const copy: ReportCopy = {
  kicker: 'Local report', privacy: 'Private', size: 'Size', files: 'Files', folders: 'Folders',
  categories: 'Categories', category: 'Category', count: 'Count', largest: 'Largest',
  relativePath: 'Relative path', risk: 'Risk',
};

describe('offline report', () => {
  it('escapes scan metadata and emits a self-contained RTL document', async () => {
    const session: ScanSession = {
      id: 'report', rootName: '<script>alert(1)</script>', source: 'import', status: 'imported',
      createdAt: 1, updatedAt: 1, completedAt: 1,
      metrics: { bytes: 1024, files: 1, folders: 1, errors: 0, elapsedMs: 1, itemsPerSecond: 1, currentPath: '' },
      categories: [{ category: 'documents', bytes: 1024, count: 1 }],
      topItems: [{
        id: 'report:file', sessionId: 'report', parentPath: '', relativePath: '<file>.txt', name: '<file>.txt',
        kind: 'file', size: 1024, modifiedAt: 1, extension: 'txt', category: 'documents', depth: 0,
        ruleIds: [], risk: 'none',
      }],
      schemaVersion: 1,
    };
    const report = createOfflineReport(session, 'fa-IR', copy);
    const html = await report.blob.text();
    expect(html).toContain('dir="rtl"');
    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>alert');
  });
});
