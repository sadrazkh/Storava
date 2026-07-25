import { createApp } from 'vue';
import LandingApp from '@/components/LandingApp.vue';
import { registerServiceWorker } from '@/services/pwaService';

const root = document.querySelector<HTMLElement>('[data-vue-island="landing"]');
if (root) {
  createApp(LandingApp).mount(root);
}

void registerServiceWorker();
