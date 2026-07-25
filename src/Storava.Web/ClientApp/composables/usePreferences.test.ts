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
