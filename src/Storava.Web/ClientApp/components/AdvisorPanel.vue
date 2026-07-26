<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { getAdvisorMessages } from '@/localization/advisorMessages';
import { dispositionLabel, getExplorerMessages } from '@/localization/explorerMessages';
import type { AdvisorResult, AdvisorReviewTarget, SanitizedScanSummary } from '@/models/advisor';
import type { ScanSession } from '@/models/scan';
import { usePreferences } from '@/composables/usePreferences';
import { createAdvisorHtmlReport, createAdvisorJsonReport } from '@/services/advisorReportService';
import { buildSanitizedSummary } from '@/services/advisorSanitizer';
import {
  loadAdvisorSettings,
  normalizeAdvisorSettings,
  saveAdvisorSettings,
} from '@/services/advisorSettings';
import { downloadExport } from '@/services/exportImportService';
import { OpenRouterAdvisorProvider } from '@/services/openRouterProvider';

const props = defineProps<{ session: ScanSession | null; storedResult: AdvisorResult | null }>();
const emit = defineEmits<{
  result: [value: AdvisorResult];
  openTarget: [value: AdvisorReviewTarget];
}>();
const { locale } = usePreferences();
const copy = computed(() => getAdvisorMessages(locale.value));
const explorerCopy = computed(() => getExplorerMessages(locale.value));
const settings = ref(loadAdvisorSettings(locale.value));
const apiKey = ref('');
const payload = ref<SanitizedScanSummary | null>(null);
const payloadAccepted = ref(false);
const previewFingerprint = ref('');
const result = ref<AdvisorResult | null>(null);
const error = ref('');
const isPreparing = ref(false);
const isSending = ref(false);
const provider = new OpenRouterAdvisorProvider();

const formattedPayload = computed(() => payload.value ? JSON.stringify(payload.value, null, 2) : '');
const dataProfileDescription = computed(() => {
  if (settings.value.dataProfile === 'essential') return copy.value.dataProfileEssentialBody;
  if (settings.value.dataProfile === 'detailed') return copy.value.dataProfileDetailedBody;
  return copy.value.dataProfileBalancedBody;
});
const canSend = computed(() =>
  settings.value.enabled
  && Boolean(props.session)
  && Boolean(payload.value)
  && payloadAccepted.value
  && apiKey.value.trim().length >= 12
  && !isSending.value);

function settingsFingerprint(): string {
  return JSON.stringify(normalizeAdvisorSettings(settings.value, settings.value.preferredLanguage));
}

function resetPreview(): void {
  payload.value = null;
  payloadAccepted.value = false;
  previewFingerprint.value = '';
  result.value = null;
  error.value = '';
}

watch(settings, () => {
  saveAdvisorSettings(settings.value);
  resetPreview();
}, { deep: true });

watch(() => props.session?.id, resetPreview);
watch(() => props.storedResult, (stored) => {
  if (stored) result.value = stored;
}, { immediate: true });

async function preparePreview(): Promise<void> {
  if (!props.session || !settings.value.enabled) return;
  isPreparing.value = true;
  error.value = '';
  result.value = null;
  payloadAccepted.value = false;
  try {
    const normalized = normalizeAdvisorSettings(settings.value, settings.value.preferredLanguage);
    payload.value = await buildSanitizedSummary(props.session, normalized);
    previewFingerprint.value = JSON.stringify(normalized);
  } catch (previewError) {
    payload.value = null;
    error.value = `${copy.value.previewFailed}: ${previewError instanceof Error ? previewError.message : ''}`;
  } finally {
    isPreparing.value = false;
  }
}

async function requestAdvice(): Promise<void> {
  if (
    !canSend.value
    || !payload.value
    || previewFingerprint.value !== settingsFingerprint()
  ) {
    error.value = copy.value.invalidPreview;
    return;
  }
  isSending.value = true;
  error.value = '';
  result.value = null;
  try {
    const advice = await provider.analyze(apiKey.value, settings.value, payload.value);
    result.value = advice;
    emit('result', advice);
  } catch (requestError) {
    error.value = `${copy.value.requestFailed}: ${requestError instanceof Error ? requestError.message : ''}`;
  } finally {
    isSending.value = false;
  }
}

function reportCopy() {
  return {
    reportLabel: copy.value.reportLabel,
    generatedWith: copy.value.generatedWith,
    privacy: copy.value.privacyReport,
    summary: copy.value.reportSummary,
    findings: copy.value.findings,
    priorities: copy.value.priorities,
    reviewTargets: explorerCopy.value.aiMapTitle,
    signal: explorerCopy.value.localTag,
    disposition: explorerCopy.value.reviewFilter,
    cautions: copy.value.cautions,
    confidence: copy.value.confidence,
    evidence: copy.value.evidence,
    disclaimer: copy.value.disclaimer,
    category: copy.value.category,
    count: copy.value.count,
    size: copy.value.size,
  };
}

function downloadJsonReport(): void {
  if (!result.value || !payload.value || !settings.value.allowReportGeneration) {
    error.value = copy.value.reportDisabled;
    return;
  }
  const report = createAdvisorJsonReport(result.value, payload.value, settings.value.preferredLanguage);
  downloadExport(report.blob, report.fileName);
}

function downloadHtmlReport(): void {
  if (!result.value || !payload.value || !settings.value.allowReportGeneration) {
    error.value = copy.value.reportDisabled;
    return;
  }
  const report = createAdvisorHtmlReport(
    result.value,
    payload.value,
    settings.value.preferredLanguage,
    reportCopy(),
  );
  downloadExport(report.blob, report.fileName);
}

onBeforeUnmount(() => {
  apiKey.value = '';
});
</script>

<template>
  <section class="advisor">
    <header class="advisor__hero">
      <div>
        <p>{{ copy.kicker }}</p>
        <h1>{{ copy.title }}</h1>
        <span>{{ copy.intro }}</span>
      </div>
      <b>{{ copy.phaseLabel }}</b>
    </header>

    <section v-if="!session" class="advisor__empty">
      <span aria-hidden="true">AI</span>
      <div><h2>{{ copy.noSessionTitle }}</h2><p>{{ copy.noSessionBody }}</p></div>
    </section>

    <section class="advisor__enable">
      <label class="advisor-switch">
        <input v-model="settings.enabled" type="checkbox">
        <span aria-hidden="true" />
        <strong>{{ copy.enableAi }}</strong>
      </label>
      <p>{{ copy.enableAiBody }}</p>
    </section>

    <template v-if="settings.enabled">
      <div class="advisor__columns">
        <section class="advisor-card advisor-settings">
          <header><span>01</span><h2>{{ copy.settingsTitle }}</h2></header>

          <fieldset class="advisor-profiles">
            <legend>{{ copy.dataProfile }}</legend>
            <div>
              <label :data-selected="settings.dataProfile === 'essential'">
                <input v-model="settings.dataProfile" type="radio" value="essential">
                <strong>{{ copy.dataProfileEssential }}</strong>
              </label>
              <label :data-selected="settings.dataProfile === 'balanced'">
                <input v-model="settings.dataProfile" type="radio" value="balanced">
                <strong>{{ copy.dataProfileBalanced }}</strong>
              </label>
              <label :data-selected="settings.dataProfile === 'detailed'">
                <input v-model="settings.dataProfile" type="radio" value="detailed">
                <strong>{{ copy.dataProfileDetailed }}</strong>
              </label>
            </div>
            <p>{{ dataProfileDescription }}</p>
          </fieldset>

          <label class="advisor-field advisor-field--wide">
            <span>{{ copy.apiKey }}</span>
            <div>
              <input
                v-model="apiKey"
                type="password"
                autocomplete="off"
                spellcheck="false"
                :placeholder="copy.apiKeyPlaceholder"
                data-testid="openrouter-key"
              >
              <button type="button" @click="apiKey = ''">{{ copy.clearKey }}</button>
            </div>
            <small>{{ copy.apiKeyHelp }}</small>
          </label>

          <div class="advisor-settings__grid">
            <label class="advisor-field">
              <span>{{ copy.model }}</span>
              <input v-model.trim="settings.model" type="text" spellcheck="false">
            </label>
            <label class="advisor-field">
              <span>{{ copy.preferredLanguage }}</span>
              <select v-model="settings.preferredLanguage">
                <option value="en-US">{{ copy.english }}</option>
                <option value="fa-IR">{{ copy.persian }}</option>
              </select>
            </label>
            <label class="advisor-field advisor-field--wide">
              <span>{{ copy.baseUrl }}</span>
              <input v-model.trim="settings.baseUrl" type="url" spellcheck="false">
              <small>{{ copy.baseUrlHelp }}</small>
            </label>
            <label class="advisor-field">
              <span>{{ copy.temperature }} · {{ settings.temperature.toFixed(1) }}</span>
              <input v-model.number="settings.temperature" type="range" min="0" max="1" step="0.1">
            </label>
            <label class="advisor-field">
              <span>{{ copy.maxTokens }}</span>
              <input v-model.number="settings.maxTokens" type="number" min="256" max="4096" step="128">
            </label>
            <label class="advisor-field">
              <span>{{ copy.timeout }}</span>
              <input
                :value="Math.round(settings.timeoutMs / 1000)"
                type="number"
                min="10"
                max="120"
                @input="settings.timeoutMs = Number(($event.target as HTMLInputElement).value) * 1000"
              >
            </label>
          </div>

          <div class="advisor-toggles">
            <label><input v-model="settings.includePathShape" type="checkbox"><span><strong>{{ copy.includePathShape }}</strong><small>{{ copy.includePathShapeBody }}</small></span></label>
            <label><input v-model="settings.allowUnknownFolderAnalysis" type="checkbox"><span><strong>{{ copy.allowUnknown }}</strong><small>{{ copy.allowUnknownBody }}</small></span></label>
            <label><input v-model="settings.allowReportGeneration" type="checkbox"><span><strong>{{ copy.allowReports }}</strong><small>{{ copy.allowReportsBody }}</small></span></label>
            <label><input v-model="settings.requireZeroDataRetention" type="checkbox"><span><strong>{{ copy.requireZdr }}</strong><small>{{ copy.requireZdrBody }}</small></span></label>
          </div>
          <p class="advisor-settings__saved">{{ copy.settingsSaved }}</p>
        </section>

        <aside class="advisor-card advisor-boundary">
          <header><span>02</span><h2>{{ copy.privacyTitle }}</h2></header>
          <article>
            <b>×</b><div><strong>{{ copy.neverSent }}</strong><p>{{ copy.neverSentList }}</p></div>
          </article>
          <article>
            <b>✓</b><div><strong>{{ copy.sentAfterConsent }}</strong><p>{{ copy.sentAfterConsentList }}</p></div>
          </article>
          <button class="button button--primary" type="button" :disabled="isPreparing || !session" @click="preparePreview">
            {{ isPreparing ? copy.preparing : payload ? copy.resetPreview : copy.preparePreview }}
          </button>
        </aside>
      </div>

      <section v-if="payload" class="advisor-preview">
        <header><div><span>03</span><h2>{{ copy.previewTitle }}</h2></div><p>{{ copy.previewBody }}</p></header>
        <pre data-testid="advisor-payload">{{ formattedPayload }}</pre>
        <label class="advisor-consent">
          <input v-model="payloadAccepted" type="checkbox">
          <span>{{ copy.reviewConsent }}</span>
        </label>
        <button class="button button--primary" type="button" :disabled="!canSend" @click="requestAdvice">
          {{ isSending ? copy.sending : copy.send }}
        </button>
      </section>

      <p v-if="error" class="advisor-error" role="alert">{{ error }}</p>

      <section v-if="result" class="advisor-result" data-testid="advisor-result">
        <header>
          <div><p>{{ copy.resultKicker }}</p><h2>{{ result.title }}</h2><span>{{ result.executiveSummary }}</span></div>
          <small>{{ result.model }}</small>
        </header>

        <div class="advisor-result__grid">
          <section>
            <h3>{{ copy.findings }}</h3>
            <article v-for="finding in result.findings" :key="finding.title" class="advisor-finding" :data-risk="finding.risk">
              <div><b>{{ finding.risk }}</b><strong>{{ finding.title }}</strong><small>{{ copy.confidence }} {{ Math.round(finding.confidence * 100) }}%</small></div>
              <p><span>{{ copy.evidence }}</span>{{ finding.evidence }}</p>
            </article>
          </section>
          <aside>
            <h3>{{ copy.priorities }}</h3>
            <ol><li v-for="priority in result.priorities" :key="priority.title"><strong>{{ priority.title }}</strong><p>{{ priority.reason }}</p><small>{{ copy.confidence }} {{ Math.round(priority.confidence * 100) }}%</small></li></ol>
          </aside>
        </div>

        <section v-if="result.reviewTargets.length" class="advisor-targets">
          <header>
            <div><h3>{{ explorerCopy.aiMapTitle }}</h3><p>{{ explorerCopy.aiMapBody }}</p></div>
            <small>{{ explorerCopy.aiLocalBoundary }}</small>
          </header>
          <div>
            <article
              v-for="target in result.reviewTargets"
              :key="`${target.signal}-${target.disposition}`"
              :data-disposition="target.disposition"
            >
              <span>{{ dispositionLabel(target.disposition, explorerCopy) }}</span>
              <strong>{{ target.signal }}</strong>
              <p>{{ target.rationale }}</p>
              <small>{{ copy.confidence }} {{ Math.round(target.confidence * 100) }}%</small>
              <button class="button button--quiet" type="button" @click="emit('openTarget', target)">
                {{ explorerCopy.showMatches }}
              </button>
            </article>
          </div>
        </section>

        <div class="advisor-result__notes">
          <section><h3>{{ copy.cautions }}</h3><ul><li v-for="caution in result.cautions" :key="caution">{{ caution }}</li></ul></section>
          <section><h3>{{ copy.privacyNote }}</h3><p>{{ result.privacyNote }}</p><h3>{{ copy.disclaimer }}</h3><p>{{ result.disclaimer }}</p></section>
        </div>
        <footer v-if="settings.allowReportGeneration">
          <button class="button button--quiet" type="button" @click="downloadJsonReport">{{ copy.downloadJson }}</button>
          <button class="button button--primary" type="button" @click="downloadHtmlReport">{{ copy.downloadHtml }}</button>
        </footer>
      </section>
    </template>
  </section>
</template>

<style scoped>
.advisor{display:grid;gap:1.4rem}.advisor__hero{display:flex;justify-content:space-between;gap:2rem;align-items:end;padding:1.2rem 0 1.8rem;border-bottom:1px solid var(--line)}.advisor__hero>div{max-width:760px}.advisor__hero p,.advisor-result>header p{margin:0 0 .65rem;color:var(--pine-bright);font-size:.74rem;font-weight:800;letter-spacing:.14em}.advisor__hero h1{max-width:720px;margin:0;color:var(--ink);font-size:clamp(2.2rem,5vw,5.2rem);line-height:.96;letter-spacing:-.045em}.advisor__hero span{display:block;max-width:68ch;margin-top:1rem;color:var(--muted);line-height:1.7}.advisor__hero>b{padding:.5rem .75rem;border:1px solid var(--line);color:var(--muted);font-size:.75rem;white-space:nowrap}.advisor__empty,.advisor__enable{display:flex;align-items:center;gap:1rem;padding:1.4rem;border:1px solid var(--line);background:var(--surface)}.advisor__empty>span{display:grid;place-items:center;width:3.5rem;height:3.5rem;border-radius:50%;background:var(--ink);color:var(--lime);font-weight:900}.advisor__empty h2,.advisor__empty p,.advisor__enable p{margin:.15rem 0}.advisor__empty p,.advisor__enable p{color:var(--muted)}.advisor__enable{justify-content:space-between}.advisor-switch{display:flex;align-items:center;gap:.75rem;cursor:pointer}.advisor-switch input{position:absolute;opacity:0}.advisor-switch>span{position:relative;width:3.2rem;height:1.8rem;border-radius:2rem;background:var(--line);pointer-events:none;transition:.2s}.advisor-switch>span::after{content:"";position:absolute;inset:.25rem auto .25rem .25rem;width:1.3rem;border-radius:50%;background:white;box-shadow:0 2px 8px #0002;transition:.2s}.advisor-switch input:checked+span{background:var(--pine-bright)}.advisor-switch input:checked+span::after{transform:translateX(1.4rem)}[dir=rtl] .advisor-switch input:checked+span::after{transform:translateX(-1.4rem)}.advisor__columns{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(280px,.7fr);gap:1.2rem;align-items:start}.advisor-card{padding:1.3rem;border:1px solid var(--line);background:var(--surface)}.advisor-card>header{display:flex;align-items:center;gap:.8rem;margin-bottom:1.2rem}.advisor-card>header span,.advisor-preview>header span{display:grid;place-items:center;width:2rem;height:2rem;border-radius:50%;background:var(--ink);color:var(--lime);font-size:.72rem;font-weight:900}.advisor-card h2,.advisor-preview h2{margin:0;font-size:1.2rem}.advisor-profiles{margin:0 0 1rem;padding:0;border:0}.advisor-profiles legend{margin-bottom:.55rem;color:var(--ink);font-size:.78rem;font-weight:800}.advisor-profiles>div{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.5rem}.advisor-profiles label{display:grid;min-height:3.1rem;place-items:center;padding:.6rem;border:1px solid var(--line);background:var(--paper);cursor:pointer;text-align:center}.advisor-profiles label[data-selected=true]{border-color:var(--pine-bright);background:color-mix(in srgb,var(--lime),transparent 82%);box-shadow:inset 0 -3px var(--pine-bright)}.advisor-profiles input{position:absolute;opacity:0}.advisor-profiles strong{font-size:.75rem}.advisor-profiles p{min-height:3em;margin:.65rem 0 0;color:var(--muted);font-size:.74rem;line-height:1.5}.advisor-settings__grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem;margin-top:1rem}.advisor-field{display:grid;gap:.45rem}.advisor-field--wide{grid-column:1/-1}.advisor-field>span{color:var(--ink);font-size:.78rem;font-weight:800}.advisor-field input,.advisor-field select{width:100%;min-height:2.75rem;padding:.62rem .75rem;border:1px solid var(--line);border-radius:0;background:var(--paper);color:var(--ink);font:inherit}.advisor-field>div{display:grid;grid-template-columns:1fr auto}.advisor-field>div input{min-width:0}.advisor-field>div button{border:1px solid var(--line);border-inline-start:0;background:transparent;color:var(--ink);padding:0 .8rem}.advisor-field small{color:var(--muted);line-height:1.5}.advisor-toggles{display:grid;gap:.65rem;margin-top:1.2rem}.advisor-toggles label{display:grid;grid-template-columns:auto 1fr;gap:.65rem;align-items:start;padding:.75rem;border:1px solid var(--line);cursor:pointer}.advisor-toggles input{margin-top:.25rem;accent-color:var(--pine-bright)}.advisor-toggles span{display:grid;gap:.25rem}.advisor-toggles small,.advisor-settings__saved{color:var(--muted);line-height:1.45}.advisor-settings__saved{margin:.9rem 0 0;font-size:.78rem}.advisor-boundary{position:sticky;top:5.5rem}.advisor-boundary article{display:grid;grid-template-columns:2rem 1fr;gap:.7rem;padding:1rem 0;border-top:1px solid var(--line)}.advisor-boundary article>b{display:grid;place-items:center;width:1.7rem;height:1.7rem;border:1px solid currentColor;border-radius:50%;color:var(--danger)}.advisor-boundary article:nth-of-type(2)>b{color:var(--pine-bright)}.advisor-boundary p{margin:.3rem 0;color:var(--muted);font-size:.83rem;line-height:1.6}.advisor-boundary .button{width:100%;margin-top:.6rem}.advisor-preview{display:grid;gap:1rem;padding:1.3rem;border:1px solid var(--line);background:#071a1c;color:#eff6ef}.advisor-preview>header{display:flex;justify-content:space-between;gap:1.5rem;align-items:start}.advisor-preview>header>div{display:flex;align-items:center;gap:.7rem}.advisor-preview>header span{background:var(--lime);color:#071a1c}.advisor-preview>header p{max-width:54ch;margin:0;color:#aebbb7;font-size:.85rem}.advisor-preview pre{max-height:420px;margin:0;padding:1rem;overflow:auto;border:1px solid #ffffff24;background:#031112;color:#c8f0bd;font:12px/1.65 ui-monospace,SFMono-Regular,Consolas,monospace;text-align:left;direction:ltr}.advisor-consent{display:flex;gap:.65rem;align-items:start;color:#eaf1ec;line-height:1.55;cursor:pointer}.advisor-consent input{margin-top:.3rem;accent-color:var(--lime)}.advisor-preview>.button{justify-self:start}.advisor-error{padding:1rem;border:1px solid color-mix(in srgb,var(--danger),transparent 50%);background:color-mix(in srgb,var(--danger),transparent 90%);color:var(--danger)}.advisor-result{display:grid;gap:1.5rem;padding:clamp(1.2rem,3vw,2.2rem);border:1px solid var(--line);background:var(--surface)}.advisor-result>header{display:flex;justify-content:space-between;gap:2rem}.advisor-result>header>div{max-width:760px}.advisor-result h2{margin:.2rem 0 .8rem;font-size:clamp(1.8rem,4vw,3.5rem);line-height:1}.advisor-result>header span{color:var(--muted);line-height:1.7}.advisor-result>header small{color:var(--muted);white-space:nowrap}.advisor-result__grid{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(260px,.7fr);gap:1.5rem}.advisor-result h3{font-size:.8rem;letter-spacing:.12em;text-transform:uppercase}.advisor-finding{padding:1rem 0;border-top:1px solid var(--line)}.advisor-finding>div{display:grid;grid-template-columns:auto 1fr auto;gap:.7rem;align-items:center}.advisor-finding>div>b{padding:.2rem .45rem;background:color-mix(in srgb,var(--amber),transparent 45%);color:var(--ink);font-size:.68rem;text-transform:uppercase}.advisor-finding[data-risk=high]>div>b{background:var(--danger);color:white}.advisor-finding[data-risk=low]>div>b{background:var(--lime)}.advisor-finding small,.advisor-result__grid aside small{color:var(--muted)}.advisor-finding p{display:grid;gap:.2rem;margin:.7rem 0 0;color:var(--muted)}.advisor-finding p span{color:var(--ink);font-size:.72rem;font-weight:800;text-transform:uppercase}.advisor-result__grid ol{padding-inline-start:1.3rem}.advisor-result__grid li{padding:.7rem 0}.advisor-result__grid li p{margin:.3rem 0;color:var(--muted)}.advisor-targets{padding:1.2rem;border:1px solid var(--line);background:color-mix(in srgb,var(--lime),transparent 92%)}.advisor-targets>header{display:flex;justify-content:space-between;gap:1rem;align-items:start}.advisor-targets h3,.advisor-targets p{margin:.15rem 0}.advisor-targets>header p,.advisor-targets>header small{max-width:62ch;color:var(--muted);line-height:1.55}.advisor-targets>div{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:.75rem;margin-top:1rem}.advisor-targets article{display:grid;gap:.5rem;padding:1rem;border:1px solid var(--line);background:var(--surface)}.advisor-targets article>span{justify-self:start;padding:.2rem .5rem;background:var(--lime);color:#071a1c;font-size:.68rem;font-weight:900;text-transform:uppercase}.advisor-targets article[data-disposition=investigate]>span{background:var(--amber)}.advisor-targets article p{min-height:3.2em;color:var(--muted);font-size:.84rem;line-height:1.55}.advisor-targets article small{color:var(--muted)}.advisor-targets .button{justify-self:start}.advisor-result__notes{display:grid;grid-template-columns:1fr 1fr;gap:1rem}.advisor-result__notes section{padding:1rem;border:1px solid var(--line)}.advisor-result__notes p,.advisor-result__notes li{color:var(--muted);line-height:1.6}.advisor-result>footer{display:flex;justify-content:flex-end;gap:.7rem}.button:disabled{cursor:not-allowed;opacity:.45}@media(max-width:980px){.advisor__columns,.advisor-result__grid{grid-template-columns:1fr}.advisor-boundary{position:static}}@media(max-width:700px){.advisor__hero,.advisor__enable,.advisor-preview>header,.advisor-result>header,.advisor-targets>header{align-items:start;flex-direction:column}.advisor__hero>b{white-space:normal}.advisor-settings__grid,.advisor-result__notes{grid-template-columns:1fr}.advisor-profiles>div{grid-template-columns:1fr}.advisor-profiles p{min-height:0}.advisor-field--wide{grid-column:auto}.advisor-preview>header>div{align-items:start}.advisor-result>footer{flex-direction:column}.advisor-result>footer .button{width:100%}}
</style>
