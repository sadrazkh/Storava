import { createApp } from 'vue';
import ScanApp from '@/components/ScanApp.vue';
import { registerServiceWorker } from '@/services/pwaService';

const root = document.querySelector<HTMLElement>('[data-vue-island="scan"]');
if (root) createApp(ScanApp).mount(root);

void registerServiceWorker();
