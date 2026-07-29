import type { Locale } from '@/localization/messages';
import type { AdvisorSettings } from '@/models/advisor';

const storageKey = 'storava.advisor.settings.v1';
export const defaultOpenRouterBaseUrl = 'https://openrouter.ai/api/v1';

export function createDefaultAdvisorSettings(locale: Locale): AdvisorSettings {
  return {
    enabled: false,
    dataProfile: 'balanced',
    model: 'openrouter/free',
    baseUrl: defaultOpenRouterBaseUrl,
    temperature: 0.2,
    maxTokens: 1_200,
    timeoutMs: 45_000,
    preferredLanguage: locale,
    includePathShape: true,
    // Off unless asked for. It is the only setting here that sends anything per folder, and
    // somebody who agreed to the aggregates-only version of this should not find that agreement
    // quietly widened by an update.
    includeItemInventory: false,
    allowUnknownFolderAnalysis: false,
    allowReportGeneration: true,
    requireZeroDataRetention: true,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function finiteNumber(value: unknown, fallback: number, minimum: number, maximum: number): number {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.min(maximum, Math.max(minimum, value))
    : fallback;
}

function booleanValue(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

export function normalizeOpenRouterBaseUrl(value: string): string {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error('OpenRouter base URL is invalid.');
  }
  const permittedHosts = new Set(['openrouter.ai', 'eu.openrouter.ai']);
  const path = url.pathname.replace(/\/+$/, '');
  if (url.protocol !== 'https:' || !permittedHosts.has(url.hostname) || path !== '/api/v1' || url.search || url.hash) {
    throw new Error('Only the official OpenRouter API base URLs are allowed.');
  }
  return `${url.origin}/api/v1`;
}

export function normalizeAdvisorSettings(value: unknown, locale: Locale): AdvisorSettings {
  const defaults = createDefaultAdvisorSettings(locale);
  if (!isRecord(value)) return defaults;

  let baseUrl = defaults.baseUrl;
  if (typeof value.baseUrl === 'string') {
    try {
      baseUrl = normalizeOpenRouterBaseUrl(value.baseUrl);
    } catch {
      baseUrl = defaults.baseUrl;
    }
  }

  const model = typeof value.model === 'string' && /^[\w./:~-]{1,160}$/.test(value.model.trim())
    ? value.model.trim()
    : defaults.model;
  const preferredLanguage = value.preferredLanguage === 'fa-IR' || value.preferredLanguage === 'en-US'
    ? value.preferredLanguage
    : defaults.preferredLanguage;
  const dataProfile = value.dataProfile === 'essential'
    || value.dataProfile === 'balanced'
    || value.dataProfile === 'detailed'
    ? value.dataProfile
    : defaults.dataProfile;

  return {
    enabled: booleanValue(value.enabled, defaults.enabled),
    dataProfile,
    model,
    baseUrl,
    temperature: finiteNumber(value.temperature, defaults.temperature, 0, 1),
    maxTokens: Math.round(finiteNumber(value.maxTokens, defaults.maxTokens, 256, 4_096)),
    timeoutMs: Math.round(finiteNumber(value.timeoutMs, defaults.timeoutMs, 10_000, 120_000)),
    preferredLanguage,
    includePathShape: booleanValue(value.includePathShape, defaults.includePathShape),
    includeItemInventory: booleanValue(value.includeItemInventory, defaults.includeItemInventory),
    allowUnknownFolderAnalysis: booleanValue(value.allowUnknownFolderAnalysis, defaults.allowUnknownFolderAnalysis),
    allowReportGeneration: booleanValue(value.allowReportGeneration, defaults.allowReportGeneration),
    requireZeroDataRetention: booleanValue(value.requireZeroDataRetention, defaults.requireZeroDataRetention),
  };
}

export function loadAdvisorSettings(locale: Locale): AdvisorSettings {
  try {
    const stored = localStorage.getItem(storageKey);
    return normalizeAdvisorSettings(stored ? JSON.parse(stored) as unknown : null, locale);
  } catch {
    return createDefaultAdvisorSettings(locale);
  }
}

export function saveAdvisorSettings(settings: AdvisorSettings): void {
  const normalized = normalizeAdvisorSettings(settings, settings.preferredLanguage);
  try {
    localStorage.setItem(storageKey, JSON.stringify(normalized));
  } catch {
    // Settings persistence is optional; the advisor still works in-memory.
  }
}
