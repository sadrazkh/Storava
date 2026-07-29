import { beforeEach, describe, expect, it, vi } from 'vitest';

beforeEach(() => {
  vi.resetModules();
  localStorage.clear();
  document.documentElement.removeAttribute('dir');
  document.documentElement.removeAttribute('lang');
  document.documentElement.removeAttribute('data-theme');
  vi.stubGlobal('matchMedia', vi.fn(() => ({
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })));
});

describe('usePreferences', () => {
  it('switches locale and document direction without a reload', async () => {
    const { usePreferences } = await import('@/composables/usePreferences');
    const preferences = usePreferences();

    preferences.setLocale('fa-IR');

    expect(document.documentElement.lang).toBe('fa-IR');
    expect(document.documentElement.dir).toBe('rtl');
    expect(preferences.t('privacyPromise')).toContain('فایل‌های شما');
  });

  it('persists and applies the selected theme', async () => {
    const { usePreferences } = await import('@/composables/usePreferences');
    const preferences = usePreferences();

    preferences.setTheme('dark');

    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem('storava.theme')).toBe('dark');
  });
});

/**
 * How many scans the browser keeps.
 *
 * This is the one preference here that deletes data, so what it refuses matters as much as what it
 * stores. The desktop offers the same choice in Settings and the agent as a command; a browser that
 * discarded scans with no number anyone could see would be the odd one out.
 */
describe('how many scans to keep', () => {
  it('keeps three until told otherwise', async () => {
    const { usePreferences } = await import('@/composables/usePreferences');

    expect(usePreferences().keepScans.value).toBe(3);
  });

  it('remembers the choice', async () => {
    const { usePreferences } = await import('@/composables/usePreferences');
    const preferences = usePreferences();

    preferences.setKeepScans(5);

    expect(preferences.keepScans.value).toBe(5);
    expect(localStorage.getItem('storava.keepScans')).toBe('5');
  });

  it('reads the remembered choice back on a later visit', async () => {
    localStorage.setItem('storava.keepScans', '10');
    const { usePreferences } = await import('@/composables/usePreferences');

    expect(usePreferences().keepScans.value).toBe(10);
  });

  /**
   * Nought would discard the scan just taken, and anything else stored is a value nobody was
   * offered — a hand-edited entry, or one left by a version that offered different numbers.
   */
  it.each(['0', '-1', '999', 'lots', ''])('falls back to three rather than trusting %s', async (stored) => {
    localStorage.setItem('storava.keepScans', stored);
    const { usePreferences } = await import('@/composables/usePreferences');

    expect(usePreferences().keepScans.value).toBe(3);
  });

  it('refuses to set a number that was never offered', async () => {
    const { usePreferences } = await import('@/composables/usePreferences');
    const preferences = usePreferences();

    preferences.setKeepScans(0);
    preferences.setKeepScans(4_000);

    expect(preferences.keepScans.value).toBe(3);
    expect(localStorage.getItem('storava.keepScans')).toBeNull();
  });
});
