<script setup lang="ts">
import { usePreferences } from '@/composables/usePreferences';

const { t } = usePreferences();
</script>

<template>
  <a class="brand" href="/" :aria-label="`${t('productName')} ${t('productSuffix')}`">
    <svg class="brand__mark" viewBox="0 0 42 42" aria-hidden="true">
      <rect x="1" y="1" width="40" height="40" rx="13" />
      <path d="M11 11h20v6H17v5h12v6H17v3h14v6H11z" />
      <circle cx="29" cy="14" r="3" />
    </svg>
    <span class="brand__name">{{ t('productName') }}</span>
    <span class="brand__suffix">{{ t('productSuffix') }}</span>
  </a>
</template>
