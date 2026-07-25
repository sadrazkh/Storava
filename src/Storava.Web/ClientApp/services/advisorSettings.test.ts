import { beforeEach, describe, expect, it } from 'vitest';
import type { AdvisorSettings } from '@/models/advisor';
import {
  createDefaultAdvisorSettings,
  loadAdvisorSettings,
  normalizeOpenRouterBaseUrl,
  saveAdvisorSettings,
} from '@/services/advisorSettings';

describe('advisor settings', () => {
  beforeEach(() => localStorage.clear());

  it('persists only normalized non-secret settings', () => {
    const value = {
      ...createDefaultAdvisorSettings('en-US'),
      model: 'openrouter/free',
      apiKey: 'sk-or-v1-must-never-persist',
    } as AdvisorSettings & { apiKey: string };
    saveAdvisorSettings(value);

    const stored = localStorage.getItem('storava.advisor.settings.v1') ?? '';
    expect(stored).not.toContain('must-never-persist');
    expect(stored).not.toContain('apiKey');
    expect(loadAdvisorSettings('fa-IR').model).toBe('openrouter/free');
  });

  it('allows only official HTTPS OpenRouter API bases', () => {
    expect(normalizeOpenRouterBaseUrl('https://openrouter.ai/api/v1/')).toBe('https://openrouter.ai/api/v1');
    expect(normalizeOpenRouterBaseUrl('https://eu.openrouter.ai/api/v1')).toBe('https://eu.openrouter.ai/api/v1');
    expect(() => normalizeOpenRouterBaseUrl('https://example.com/api/v1')).toThrow(/official/i);
    expect(() => normalizeOpenRouterBaseUrl('http://openrouter.ai/api/v1')).toThrow(/official/i);
  });
});
