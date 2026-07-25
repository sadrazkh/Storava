import { describe, expect, it } from 'vitest';
import { evaluateRules } from '@/rules/ruleEngine';

function item(overrides: Partial<Parameters<typeof evaluateRules>[0]> = {}) {
  return {
    name: 'file.txt',
    kind: 'file' as const,
    size: 1024,
    modifiedAt: Date.now(),
    relativePath: 'folder/file.txt',
    extension: 'txt',
    ...overrides,
  };
}

describe('storage rule engine', () => {
  it('marks generated folders without desktop path assumptions', () => {
    expect(evaluateRules(item({ name: 'node_modules', kind: 'folder', relativePath: 'project/node_modules' })))
      .toEqual({ ruleIds: ['generated-folder'], risk: 'medium' });
  });

  it('keeps the highest risk when several rules match', () => {
    const result = evaluateRules(item({ name: 'backup-old.zip', extension: 'zip', size: 3 * 1024 ** 3 }));
    expect(result.risk).toBe('high');
    expect(result.ruleIds).toEqual(expect.arrayContaining(['huge-file', 'archive', 'backup-copy']));
  });
});
