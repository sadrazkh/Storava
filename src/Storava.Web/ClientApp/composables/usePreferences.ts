import { computed, readonly, ref } from 'vue';
import { messages, type Locale, type MessageKey } from '@/localization/messages';

export type Theme = 'light' | 'dark';

const storedLocale = localStorage.getItem('storava.locale');
const storedTheme = localStorage.getItem('storava.theme');
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
    direction: computed(() => locale.value === 'fa-IR' ? 'rtl' : 'ltr'),
    setLocale,
    setTheme,
    t,
  };
}
