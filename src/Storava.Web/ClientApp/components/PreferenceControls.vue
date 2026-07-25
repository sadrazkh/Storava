<script setup lang="ts">
import { usePreferences } from '@/composables/usePreferences';

const { locale, theme, setLocale, setTheme, t } = usePreferences();
</script>

<template>
  <div class="preferences">
    <div class="segmented" role="group" :aria-label="t('languageLabel')">
      <button
        class="segmented__button"
        :class="{ 'is-active': locale === 'en-US' }"
        type="button"
        :aria-pressed="locale === 'en-US'"
        @click="setLocale('en-US')"
      >
        EN
      </button>
      <button
        class="segmented__button"
        :class="{ 'is-active': locale === 'fa-IR' }"
        type="button"
        :aria-pressed="locale === 'fa-IR'"
        @click="setLocale('fa-IR')"
      >
        فا
      </button>
    </div>
    <button
      class="icon-button"
      type="button"
      :aria-label="theme === 'dark' ? t('themeLight') : t('themeDark')"
      :title="theme === 'dark' ? t('themeLight') : t('themeDark')"
      @click="setTheme(theme === 'dark' ? 'light' : 'dark')"
    >
      <svg v-if="theme === 'dark'" viewBox="0 0 24 24" aria-hidden="true">
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.42-1.42M17.66 6.34l1.41-1.41" />
      </svg>
      <svg v-else viewBox="0 0 24 24" aria-hidden="true">
        <path d="M20.5 14.5A8.5 8.5 0 0 1 9.5 3.5a8.5 8.5 0 1 0 11 11Z" />
      </svg>
    </button>
  </div>
</template>
