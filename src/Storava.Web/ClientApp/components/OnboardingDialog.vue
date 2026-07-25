<script setup lang="ts">
import { nextTick, ref, watch } from 'vue';
import CapabilityList from '@/components/CapabilityList.vue';
import { usePreferences } from '@/composables/usePreferences';
import type { BrowserCapabilities, FolderSelection } from '@/models/capabilities';
import {
  FolderSelectionCancelledError,
  selectFolder,
} from '@/services/folderPermissionService';

const props = defineProps<{
  open: boolean;
  capabilities: BrowserCapabilities;
}>();
const emit = defineEmits<{ close: [] }>();
const { t } = usePreferences();

const step = ref(0);
const privacyConfirmed = ref(false);
const selectedFolder = ref<FolderSelection | null>(null);
const selectionMessage = ref<'cancelled' | 'error' | null>(null);
const isSelecting = ref(false);
const closeButton = ref<HTMLButtonElement | null>(null);

watch(() => props.open, async (open) => {
  document.body.classList.toggle('dialog-open', open);
  if (open) {
    step.value = 0;
    selectionMessage.value = null;
    await nextTick();
    closeButton.value?.focus();
  }
});

function close(): void {
  document.body.classList.remove('dialog-open');
  emit('close');
}

function environmentTitle(): string {
  if (props.capabilities.mode === 'native') return t('environmentReadyTitle');
  if (props.capabilities.mode === 'fallback') return t('environmentFallbackTitle');
  return t('environmentBlockedTitle');
}

function environmentBody(): string {
  if (props.capabilities.mode === 'native') return t('environmentReadyBody');
  if (props.capabilities.mode === 'fallback') return t('environmentFallbackBody');
  return t('environmentBlockedBody');
}

async function chooseFolder(): Promise<void> {
  isSelecting.value = true;
  selectionMessage.value = null;
  try {
    selectedFolder.value = await selectFolder(props.capabilities);
  } catch (error) {
    selectionMessage.value = error instanceof FolderSelectionCancelledError ? 'cancelled' : 'error';
  } finally {
    isSelecting.value = false;
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="open" class="dialog-backdrop" @mousedown.self="close">
        <section
          class="onboarding"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="'onboarding-title'"
          @keydown.esc="close"
        >
          <header class="onboarding__header">
            <div>
              <span class="micro-label">{{ t('phaseBoundary') }}</span>
              <h2 id="onboarding-title">{{ t('onboardingTitle') }}</h2>
              <p>{{ t('onboardingSubtitle') }}</p>
            </div>
            <button ref="closeButton" class="icon-button" type="button" :aria-label="t('closeDialog')" @click="close">
              <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="m6 6 12 12M18 6 6 18" />
              </svg>
            </button>
          </header>

          <ol class="stepper" :aria-label="t('onboardingTitle')">
            <li v-for="(label, index) in [t('onboardingStepEnvironment'), t('onboardingStepPrivacy'), t('onboardingStepFolder')]"
                :key="label"
                :class="{ 'is-active': step === index, 'is-complete': step > index }">
              <span>{{ index + 1 }}</span>
              {{ label }}
            </li>
          </ol>

          <div class="onboarding__content">
            <section v-if="step === 0" class="onboarding__panel">
              <div class="status-emblem" :data-mode="capabilities.mode" aria-hidden="true">
                <span />
                <svg viewBox="0 0 32 32">
                  <path v-if="capabilities.mode !== 'unsupported'" d="m8 16 5 5 11-12" />
                  <path v-else d="m9 9 14 14M23 9 9 23" />
                </svg>
              </div>
              <h3>{{ environmentTitle() }}</h3>
              <p>{{ environmentBody() }}</p>
              <CapabilityList :capabilities="capabilities" />
            </section>

            <section v-else-if="step === 1" class="onboarding__panel">
              <div class="privacy-seal" aria-hidden="true">
                <svg viewBox="0 0 32 32">
                  <path d="M16 3 27 8v7c0 7-4.6 11.7-11 14C9.6 26.7 5 22 5 15V8z" />
                  <path d="m11 16 3 3 7-8" />
                </svg>
              </div>
              <h3>{{ t('privacyConfirmTitle') }}</h3>
              <p>{{ t('privacyConfirmBody') }}</p>
              <label class="consent">
                <input v-model="privacyConfirmed" type="checkbox">
                <span class="consent__box" aria-hidden="true">
                  <svg viewBox="0 0 16 16"><path d="m3 8 3 3 7-8" /></svg>
                </span>
                <span>{{ t('privacyConfirmLabel') }}</span>
              </label>
            </section>

            <section v-else class="onboarding__panel">
              <div class="folder-emblem" aria-hidden="true">
                <svg viewBox="0 0 40 40">
                  <path d="M4 10h13l4 4h15v19H4z" />
                  <path d="M4 17h32" />
                </svg>
              </div>
              <h3>{{ t('folderTitle') }}</h3>
              <p>{{ t('folderBody') }}</p>
              <button
                class="folder-picker"
                type="button"
                :disabled="capabilities.mode === 'unsupported' || isSelecting"
                @click="chooseFolder"
              >
                <span class="folder-picker__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24"><path d="M3 7h7l2 2h9v10H3z" /></svg>
                </span>
                <span>{{ t('selectFolder') }}</span>
                <svg class="folder-picker__arrow" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m9 5 7 7-7 7" />
                </svg>
              </button>
              <div v-if="selectedFolder" class="selection-result selection-result--success" role="status">
                <strong>{{ t('selectedFolder') }}</strong>
                <span>{{ t('selectedFolderBody', { name: selectedFolder.name }) }}</span>
              </div>
              <div v-else-if="selectionMessage" class="selection-result" role="status">
                {{ selectionMessage === 'cancelled' ? t('selectionCancelled') : t('selectionError') }}
              </div>
            </section>
          </div>

          <footer class="onboarding__footer">
            <button v-if="step > 0" class="button button--quiet" type="button" @click="step -= 1">
              {{ t('back') }}
            </button>
            <span v-else />
            <button
              v-if="step < 2"
              class="button button--primary"
              type="button"
              :disabled="capabilities.mode === 'unsupported' || (step === 1 && !privacyConfirmed)"
              @click="step += 1"
            >
              {{ t('continue') }}
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
            </button>
            <button v-else class="button button--quiet" type="button" @click="close">
              {{ t('closeDialog') }}
            </button>
          </footer>
        </section>
      </div>
    </Transition>
  </Teleport>
</template>
