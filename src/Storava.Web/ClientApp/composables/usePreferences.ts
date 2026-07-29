import { computed, readonly, ref } from 'vue';
import { messages, type Locale, type MessageKey } from '@/localization/messages';

export type Theme = 'light' | 'dark';

const storedLocale = localStorage.getItem('storava.locale');
const storedTheme = localStorage.getItem('storava.theme');
const storedKeepScans = localStorage.getItem('storava.keepScans');
const preferredLocale: Locale =
  storedLocale === 'fa-IR' || storedLocale === 'en-US'
    ? storedLocale
    : navigator.language.toLowerCase().startsWith('fa')
      ? 'fa-IR'
      : 'en-US';
const preferredTheme: Theme =
  storedTheme === 'light' || storedTheme === 'dark'
    ? storedTheme
    : matchMedia('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';

/**
 * A short list rather than a free number. The meaningful range is small, and a text box here would
 * invite a nought — which would mean discarding the scan just taken.
 *
 * Declared before it is read: this module builds its state as it loads, and a `const` is unusable
 * until its own line runs, so putting this below the ref made the whole module throw on import.
 */
export const KEEP_SCAN_OPTIONS = [1, 2, 3, 5, 10];

/** Anything outside the offered range is a value nobody chose, so the default stands. */
function readKeepScans(stored: string | null): number {
  const parsed = Number(stored);
  return KEEP_SCAN_OPTIONS.includes(parsed) ? parsed : 3;
}

/**
 * How many scans to keep before the older ones are discarded.
 *
 * Three by default: enough to compare a folder against how it looked last time, few enough that a
 * browser's storage stays small. It lives here rather than as a constant in the scanner because it
 * deletes data — the desktop edition offers the same choice in Settings and the agent as a command,
 * and a browser that quietly discarded scans with no way to see the number would be the odd one out.
 */
const keepScans = ref<number>(readKeepScans(storedKeepScans));

const locale = ref<Locale>(preferredLocale);
const theme = ref<Theme>(preferredTheme);

function applyPreferences(): void {
  const root = document.documentElement;
  root.lang = locale.value;
  root.dir = locale.value === 'fa-IR' ? 'rtl' : 'ltr';
  root.dataset.theme = theme.value;
  root.style.colorScheme = theme.value;
  document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
    ?.setAttribute('content', theme.value === 'dark' ? '#071a1c' : '#f2f3ea');
}

function setLocale(next: Locale): void {
  locale.value = next;
  localStorage.setItem('storava.locale', next);
  applyPreferences();
}

function setTheme(next: Theme): void {
  theme.value = next;
  localStorage.setItem('storava.theme', next);
  applyPreferences();
}

function setKeepScans(next: number): void {
  if (!KEEP_SCAN_OPTIONS.includes(next)) return;
  keepScans.value = next;
  localStorage.setItem('storava.keepScans', String(next));
}

function t(key: MessageKey, parameters?: Record<string, string>): string {
  let value: string = messages[locale.value][key];
  if (!parameters) return value;

  for (const [name, replacement] of Object.entries(parameters)) {
    value = value.replaceAll(`{${name}}`, replacement);
  }

  return value;
}

applyPreferences();

export function usePreferences() {
  return {
    locale: readonly(locale),
    theme: readonly(theme),
    keepScans: readonly(keepScans),
    direction: computed(() => locale.value === 'fa-IR' ? 'rtl' : 'ltr'),
    setLocale,
    setTheme,
    setKeepScans,
    t,
  };
}
