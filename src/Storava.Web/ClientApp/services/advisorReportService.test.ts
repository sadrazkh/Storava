import { describe, expect, it } from 'vitest';
import type { AdvisorResult, SanitizedScanSummary } from '@/models/advisor';
import { createAdvisorHtmlReport, createAdvisorJsonReport } from '@/services/advisorReportService';

const result: AdvisorResult = {
  title: '<script>unsafe</script>',
  executiveSummary: 'Summary',
  findings: [{ title: 'Finding', evidence: '<img src=x>', risk: 'low', confidence: 0.8 }],
  priorities: [{ title: 'Review', reason: 'Human review only', confidence: 0.7 }],
  reviewTargets: [{ signal: 'archive', disposition: 'archive-candidate', rationale: 'Review archives', confidence: 0.75 }],
  itemTargets: [],
  cautions: ['Do not infer contents.'],
  disclaimer: 'No automatic actions.',
  privacyNote: 'Aggregates only.',
  model: 'openrouter/free',
  generatedAt: '2026-07-25T12:00:00.000Z',
};

const summary: SanitizedScanSummary = {
  schemaVersion: 2,
  dataProfile: 'balanced',
  privacy: {
    containsFileContent: false,
    containsFileNames: false,
    containsFolderNames: false,
    containsAbsolutePaths: false,
    containsRelativePaths: false,
    containsApiKeys: false,
    containsAnonymousInventory: false,
  },
  scan: { status: 'completed', totalBytes: 1, fileCount: 1, folderCount: 0, accessErrorCount: 0, elapsedMilliseconds: 1 },
  categories: [{ category: 'documents', bytes: 1, count: 1 }],
  riskCounts: { none: 1, low: 0, medium: 0, high: 0 },
  ruleMatches: [],
  sizeDistribution: [],
  ageDistribution: [],
};

describe('advisor reports', () => {
  it('creates escaped printable HTML and key-free JSON', async () => {
    const htmlReport = createAdvisorHtmlReport(result, summary, 'fa-IR', {
      reportLabel: 'گزارش', generatedWith: 'مدل', privacy: 'خصوصی', summary: 'خلاصه',
      findings: 'یافته‌ها', priorities: 'اولویت‌ها', cautions: 'احتیاط‌ها',
      reviewTargets: 'هدف‌های بررسی', signal: 'نشانه', disposition: 'نوع پیشنهاد',
      confidence: 'اطمینان', evidence: 'شواهد', disclaimer: 'سلب مسئولیت',
      category: 'دسته', count: 'تعداد', size: 'حجم',
    });
    const html = await htmlReport.blob.text();
    expect(html).toContain('dir="rtl"');
    expect(html).toContain('&lt;script&gt;unsafe&lt;/script&gt;');
    expect(html).not.toContain('<script>unsafe');

    const jsonReport = createAdvisorJsonReport(result, summary, 'fa-IR');
    const json = await jsonReport.blob.text();
    expect(json).toContain('"privacy": "aggregate-metadata-only"');
    expect(json).toContain('"signal": "archive"');
    expect(json).not.toContain('sk-or-v1');
  });
});
