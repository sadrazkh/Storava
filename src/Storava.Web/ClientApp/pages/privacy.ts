import { createApp } from 'vue';
import PrivacyApp from '@/components/PrivacyApp.vue';
import { registerServiceWorker } from '@/services/pwaService';

const root = document.querySelector<HTMLElement>('[data-vue-island="privacy"]');
if (root) {
  createApp(PrivacyApp).mount(root);
}

void registerServiceWorker();
