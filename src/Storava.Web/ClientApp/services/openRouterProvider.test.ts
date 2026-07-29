import { describe, expect, it, vi } from 'vitest';
import type { SanitizedScanSummary } from '@/models/advisor';
import { createDefaultAdvisorSettings } from '@/services/advisorSettings';
import {
  buildOpenRouterRequest,
  OpenRouterAdvisorProvider,
  parseAdvisorResponse,
} from '@/services/openRouterProvider';

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
  reviewTargets: [],
  itemTargets: [],
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
    expect(init.body).toContain('exact counts and byte totals');
    const request = JSON.parse(init.body) as {
      messages: Array<{ role: string; content: string }>;
    };
    const userMessage = request.messages.find((message) => message.role === 'user');
    expect(userMessage).toBeDefined();
    expect(JSON.parse(userMessage?.content ?? '{}')).toMatchObject({
      analysisContract: { dataProfile: 'balanced' },
      sanitizedScanSummary: { dataProfile: 'balanced' },
    });
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
    expect(() => parseAdvisorResponse({
      ...validAdvice,
      reviewTargets: [{
        signal: 'unsupported-rule',
        disposition: 'cleanup-candidate',
        rationale: 'Invalid signal',
        confidence: 0.8,
      }],
    })).toThrow(/signal/i);
  });

  it('does not place the API key in the serializable request object', () => {
    const serialized = JSON.stringify(buildOpenRouterRequest(createDefaultAdvisorSettings('en-US'), summary));
    expect(serialized).not.toContain('sk-or-v1-private-test-key');
    expect(serialized).not.toContain('Authorization');
  });
});

/**
 * The advisor may now be given an anonymous inventory, so it can say something about one folder
 * rather than about every folder matching a rule.
 *
 * What matters here is the boundary. The model is handed reference numbers and figures and nothing
 * that identifies anything, and its answers come back addressed by those same references — so the
 * only thing that can turn one back into a folder is the page that minted it.
 */
describe('advising on individual items', () => {
  const withInventory = {
    ...validAdvice,
    itemTargets: [
      { ref: 'f1', disposition: 'cleanup-candidate', rationale: 'Large and regenerable.', confidence: 0.8 },
    ],
  };

  it('accepts advice addressed to an inventory reference', () => {
    const parsed = parseAdvisorResponse(withInventory);

    expect(parsed.itemTargets).toEqual([
      { ref: 'f1', disposition: 'cleanup-candidate', rationale: 'Large and regenerable.', confidence: 0.8 },
    ]);
  });

  /**
   * The mapping back is the page's, and it is what keeps the reference meaningless to everyone
   * else. A parser that quietly accepted an item id would let the model address a folder directly.
   */
  it('refuses an item target carrying anything beyond its four fields', () => {
    expect(() => parseAdvisorResponse({
      ...validAdvice,
      itemTargets: [{
        ref: 'f1',
        itemId: 'real-scan-item-id',
        disposition: 'cleanup-candidate',
        rationale: 'Large and regenerable.',
        confidence: 0.8,
      }],
    })).toThrow(/unsupported/i);
  });

  it('refuses an item target with an invalid disposition', () => {
    expect(() => parseAdvisorResponse({
      ...validAdvice,
      itemTargets: [{ ref: 'f1', disposition: 'delete-now', rationale: 'No.', confidence: 0.8 }],
    })).toThrow(/disposition/i);
  });

  it('refuses more item targets than the schema allows', () => {
    expect(() => parseAdvisorResponse({
      ...validAdvice,
      itemTargets: Array.from({ length: 13 }, (_, index) => ({
        ref: `f${index + 1}`,
        disposition: 'investigate',
        rationale: 'Worth a look.',
        confidence: 0.5,
      })),
    })).toThrow(/item targets/i);
  });

  /** An answer with the field missing altogether is malformed, not an answer with no targets. */
  it('refuses a response that omits item targets', () => {
    const withoutItemTargets = { ...validAdvice };
    delete (withoutItemTargets as Partial<typeof validAdvice>).itemTargets;

    expect(() => parseAdvisorResponse(withoutItemTargets)).toThrow();
  });

  /**
   * The model is told, in the request itself, that it has no names to work from. Without this it
   * has every incentive to invent one — a rationale reading "your node_modules folder" would be a
   * guess presented as knowledge.
   */
  it('tells the model it has no names, extensions or paths to reason from', () => {
    const request = buildOpenRouterRequest(
      { ...createDefaultAdvisorSettings('en-US'), includeItemInventory: true },
      summary,
    );
    const system = JSON.stringify(request).toLowerCase();

    expect(system).toContain('itemtargets may reference only the ref values present in inventory');
    expect(system).toContain('never guess, invent or describe one');
  });
});
