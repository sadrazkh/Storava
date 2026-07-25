import { describe, expect, it, vi } from 'vitest';
import type { SanitizedScanSummary } from '@/models/advisor';
import { createDefaultAdvisorSettings } from '@/services/advisorSettings';
import {
  buildOpenRouterRequest,
  OpenRouterAdvisorProvider,
  parseAdvisorResponse,
} from '@/services/openRouterProvider';

const summary: SanitizedScanSummary = {
  schemaVersion: 1,
  privacy: {
    containsFileContent: false,
    containsFileNames: false,
    containsFolderNames: false,
    containsAbsolutePaths: false,
    containsRelativePaths: false,
    containsApiKeys: false,
  },
  scan: {
    status: 'completed',
    totalBytes: 42,
    fileCount: 1,
    folderCount: 0,
    accessErrorCount: 0,
    elapsedMilliseconds: 1,
  },
  categories: [{ category: 'documents', bytes: 42, count: 1 }],
  riskCounts: { none: 1, low: 0, medium: 0, high: 0 },
  ruleMatches: [],
  sizeDistribution: [{ bucket: 'under-1-mib', count: 1, bytes: 42 }],
  ageDistribution: [{ bucket: 'unknown', count: 1, bytes: 42 }],
};

const validAdvice = {
  title: 'Storage review',
  executiveSummary: 'The aggregate is small and low risk.',
  findings: [{ title: 'Document share', evidence: 'One aggregate document entry.', risk: 'low', confidence: 0.9 }],
  priorities: [{ title: 'Review growth later', reason: 'Current pressure is low.', confidence: 0.8 }],
  cautions: ['Metadata cannot determine whether a file is useful.'],
  disclaimer: 'Review evidence yourself before any file action.',
  privacyNote: 'Only aggregate metadata was analyzed.',
};

describe('OpenRouter advisor provider', () => {
  it('keeps the key out of the body and requests strict privacy-preserving structured output', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => {
      void _input;
      void _init;
      return Promise.resolve(new Response(JSON.stringify({
        model: 'example/free',
        choices: [{ message: { content: JSON.stringify(validAdvice) } }],
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    });
    const provider = new OpenRouterAdvisorProvider({ fetchImplementation: fetchMock });
    const settings = createDefaultAdvisorSettings('en-US');
    const key = 'sk-or-v1-private-test-key';

    const result = await provider.analyze(key, settings, summary);
    const call = fetchMock.mock.calls[0];
    if (!call) throw new Error('Expected OpenRouter fetch call.');
    const [url, init] = call;
    if (typeof init?.body !== 'string') throw new Error('Expected a serialized request body.');
    const headers = new Headers(init.headers);

    expect(url).toBe('https://openrouter.ai/api/v1/chat/completions');
    expect(headers.get('Authorization')).toBe(`Bearer ${key}`);
    expect(init.body).not.toContain(key);
    expect(init.body).toContain('"data_collection":"deny"');
    expect(init.body).toContain('"zdr":true');
    expect(init.body).toContain('"type":"json_schema"');
    expect(result.model).toBe('example/free');
  });

  it('rejects arbitrary key destinations before fetch', async () => {
    const fetchMock = vi.fn<typeof fetch>();
    const provider = new OpenRouterAdvisorProvider({ fetchImplementation: fetchMock });
    const settings = { ...createDefaultAdvisorSettings('en-US'), baseUrl: 'https://example.com/api/v1' };
    await expect(provider.analyze('sk-or-v1-private-test-key', settings, summary)).rejects.toThrow(/official/i);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects extra or malformed response fields after JSON parsing', () => {
    expect(() => parseAdvisorResponse({ ...validAdvice, unexpected: 'field' })).toThrow(/unsupported/i);
    expect(() => parseAdvisorResponse({
      ...validAdvice,
      findings: [{ ...validAdvice.findings[0], confidence: 2 }],
    })).toThrow(/confidence/i);
  });

  it('does not place the API key in the serializable request object', () => {
    const serialized = JSON.stringify(buildOpenRouterRequest(createDefaultAdvisorSettings('en-US'), summary));
    expect(serialized).not.toContain('sk-or-v1-private-test-key');
    expect(serialized).not.toContain('Authorization');
  });
});
