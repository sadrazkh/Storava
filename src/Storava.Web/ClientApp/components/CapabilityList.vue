<script setup lang="ts">
import { computed } from 'vue';
import { usePreferences } from '@/composables/usePreferences';
import type { BrowserCapabilities } from '@/models/capabilities';

const props = defineProps<{ capabilities: BrowserCapabilities }>();
const { t } = usePreferences();

const items = computed(() => [
  { label: t('capabilityPicker'), available: props.capabilities.nativeDirectoryPicker },
  { label: t('capabilityFallbackPicker'), available: props.capabilities.directoryInputFallback },
  { label: t('capabilityWorker'), available: props.capabilities.webWorker },
  { label: t('capabilityDatabase'), available: props.capabilities.indexedDb },
  { label: t('capabilityServiceWorker'), available: props.capabilities.serviceWorker },
  { label: t('capabilitySecure'), available: props.capabilities.secureContext },
]);
</script>

<template>
  <ul class="capability-list">
    <li v-for="item in items" :key="item.label" class="capability-list__item">
      <span class="status-dot" :class="{ 'status-dot--off': !item.available }" aria-hidden="true" />
      <span>{{ item.label }}</span>
      <svg v-if="item.available" viewBox="0 0 20 20" aria-hidden="true">
        <path d="m4 10 4 4 8-9" />
      </svg>
      <svg v-else viewBox="0 0 20 20" aria-hidden="true">
        <path d="m5 5 10 10M15 5 5 15" />
      </svg>
    </li>
  </ul>
</template>
