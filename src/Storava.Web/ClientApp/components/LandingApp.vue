<script setup lang="ts">
import { ref, watchEffect } from 'vue';
import BrandMark from '@/components/BrandMark.vue';
import CapabilityList from '@/components/CapabilityList.vue';
import OnboardingDialog from '@/components/OnboardingDialog.vue';
import PreferenceControls from '@/components/PreferenceControls.vue';
import { usePreferences } from '@/composables/usePreferences';
import { detectCapabilities } from '@/services/capabilityService';

const { t } = usePreferences();
const onboardingOpen = ref(false);
const capabilities = detectCapabilities();

watchEffect(() => {
  document.title = `${t('landingMetaTitle')} · ${t('productName')} Web`;
});

function openOnboarding(): void {
  onboardingOpen.value = true;
}
</script>

<template>
  <div class="site-shell">
    <header class="site-header">
      <div class="container site-header__inner">
        <BrandMark />
        <nav class="site-nav" :aria-label="t('productName')">
          <a href="#how">{{ t('navHow') }}</a>
          <a href="#compatibility">{{ t('navCompatibility') }}</a>
          <a href="#privacy">{{ t('navPrivacy') }}</a>
        </nav>
        <div class="site-header__actions">
          <PreferenceControls />
          <a class="button button--small button--ink" href="/scan">
            {{ t('startScan') }}
          </a>
        </div>
      </div>
    </header>

    <section class="hero">
      <div class="hero__grid" aria-hidden="true" />
      <div class="container hero__inner">
        <div class="hero__copy">
          <p class="eyebrow"><span />{{ t('eyebrow') }}</p>
          <h1>
            {{ t('heroTitleStart') }}
            <span>{{ t('heroTitleAccent') }}</span>
          </h1>
          <p class="hero__lead">{{ t('heroBody') }}</p>
          <div class="hero__actions">
            <a class="button button--primary button--large" href="/scan">
              <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="M3 7h7l2 2h9v10H3z" />
              </svg>
              {{ t('startScan') }}
            </a>
            <a class="button button--quiet button--large" href="#how">
              {{ t('seeHow') }}
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
            </a>
          </div>
          <div class="privacy-promise">
            <span class="privacy-promise__icon" aria-hidden="true">
              <svg viewBox="0 0 24 24">
                <path d="M12 2 21 6v6c0 5.5-3.8 9-9 11-5.2-2-9-5.5-9-11V6z" />
                <path d="m8 12 2.5 2.5L16 9" />
              </svg>
            </span>
            <span>
              <strong>{{ t('privacyPromise') }}</strong>
              <small>{{ t('permissionOnly') }}</small>
            </span>
          </div>
        </div>

        <div class="atlas" aria-hidden="true">
          <div class="atlas__topline">
            <span>{{ t('atlasLabel') }}</span>
            <span class="live-signal"><i />{{ t('signalPrivate') }}</span>
          </div>
          <div class="atlas__stage">
            <div class="atlas__ring atlas__ring--outer">
              <span class="atlas__node atlas__node--documents">{{ t('atlasDocuments') }}</span>
              <span class="atlas__node atlas__node--media">{{ t('atlasMedia') }}</span>
              <span class="atlas__node atlas__node--projects">{{ t('atlasProjects') }}</span>
              <span class="atlas__node atlas__node--other">{{ t('atlasOther') }}</span>
            </div>
            <div class="atlas__ring atlas__ring--middle" />
            <div class="atlas__core">
              <svg viewBox="0 0 52 52">
                <path d="M9 16h15l5 5h14v22H9z" />
                <path d="M9 23h34" />
              </svg>
              <strong>{{ t('atlasIdle') }}</strong>
              <small>{{ t('atlasCore') }}</small>
            </div>
            <div class="atlas__scanline" />
          </div>
          <div class="atlas__footer">
            <span>{{ t('atlasHint') }}</span>
            <span class="local-chip"><i />{{ t('signalLocal') }}</span>
          </div>
        </div>
      </div>
      <div class="container proof-strip">
        <span><i class="proof-strip__pulse" />{{ t('localOnly') }}</span>
        <span><i class="proof-strip__slash" />{{ t('uploadFree') }}</span>
        <span><i class="proof-strip__type" />{{ t('bilingual') }}</span>
      </div>
    </section>

    <section id="how" class="section section--paper">
      <div class="container">
        <div class="section-heading section-heading--wide">
          <p class="kicker">{{ t('introKicker') }}</p>
          <div>
            <h2>{{ t('introTitle') }}</h2>
            <p>{{ t('introBody') }}</p>
          </div>
        </div>
        <div class="feature-grid">
          <article class="feature-card feature-card--permission">
            <span class="feature-card__index">01</span>
            <div class="feature-icon feature-icon--lime">
              <svg viewBox="0 0 32 32"><path d="M16 3 27 8v7c0 7-4.6 11.7-11 14C9.6 26.7 5 22 5 15V8z" /><path d="m11 16 3 3 7-8" /></svg>
            </div>
            <h3>{{ t('featurePermissionTitle') }}</h3>
            <p>{{ t('featurePermissionBody') }}</p>
            <div class="permission-diagram" aria-hidden="true">
              <span class="permission-diagram__device" />
              <span class="permission-diagram__gate"><i /></span>
              <span class="permission-diagram__folder" />
            </div>
          </article>
          <article class="feature-card feature-card--depth">
            <span class="feature-card__index">02</span>
            <div class="feature-icon">
              <svg viewBox="0 0 32 32"><path d="M6 5v22M6 10h8v6h9M14 7v6M23 13v6M14 22h9v5" /></svg>
            </div>
            <h3>{{ t('featureDepthTitle') }}</h3>
            <p>{{ t('featureDepthBody') }}</p>
            <div class="depth-lines" aria-hidden="true">
              <span /><span /><span /><span /><span />
            </div>
          </article>
          <article class="feature-card feature-card--language">
            <span class="feature-card__index">03</span>
            <div class="language-sample" aria-hidden="true">
              <span>Space</span><i>↔</i><span>فضا</span>
            </div>
            <h3>{{ t('featureBilingualTitle') }}</h3>
            <p>{{ t('featureBilingualBody') }}</p>
          </article>
          <article class="feature-card feature-card--offline">
            <span class="feature-card__index">04</span>
            <div class="offline-orbit" aria-hidden="true">
              <span><i /></span>
              <svg viewBox="0 0 32 32"><path d="M8 5h16v22H8z" /><path d="M13 23h6" /></svg>
            </div>
            <h3>{{ t('featureOfflineTitle') }}</h3>
            <p>{{ t('featureOfflineBody') }}</p>
          </article>
        </div>
      </div>
    </section>

    <section class="section section--ink workflow">
      <div class="container">
        <div class="section-heading section-heading--inverse">
          <p class="kicker">{{ t('workflowKicker') }}</p>
          <h2>{{ t('workflowTitle') }}</h2>
        </div>
        <div class="workflow__track">
          <article>
            <span class="workflow__number">01</span>
            <div class="workflow__icon">
              <svg viewBox="0 0 32 32"><path d="M4 9h10l3 3h11v14H4z" /><path d="M16 3v10M12 7l4-4 4 4" /></svg>
            </div>
            <h3>{{ t('stepChooseTitle') }}</h3>
            <p>{{ t('stepChooseBody') }}</p>
          </article>
          <div class="workflow__connector" aria-hidden="true"><i /><i /><i /></div>
          <article>
            <span class="workflow__number">02</span>
            <div class="workflow__icon">
              <svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="11" /><path d="M16 5v11l7 5" /></svg>
            </div>
            <h3>{{ t('stepObserveTitle') }}</h3>
            <p>{{ t('stepObserveBody') }}</p>
          </article>
          <div class="workflow__connector" aria-hidden="true"><i /><i /><i /></div>
          <article>
            <span class="workflow__number">03</span>
            <div class="workflow__icon">
              <svg viewBox="0 0 32 32"><path d="M5 25 13 17l5 5 9-12" /><path d="M21 10h6v6" /></svg>
            </div>
            <h3>{{ t('stepDecideTitle') }}</h3>
            <p>{{ t('stepDecideBody') }}</p>
          </article>
        </div>
      </div>
    </section>

    <section id="compatibility" class="section section--paper capability-section">
      <div class="container capability-layout">
        <div>
          <p class="kicker">{{ t('compatibilityKicker') }}</p>
          <h2>{{ t('compatibilityTitle') }}</h2>
          <p class="section-lead">{{ t('compatibilityBody') }}</p>
          <button class="button button--ink button--large" type="button" @click="openOnboarding">
            {{ t('openOnboarding') }}
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
          </button>
        </div>
        <div class="capability-console">
          <div class="capability-console__head">
            <div class="browser-lights" aria-hidden="true"><i /><i /><i /></div>
            <span>storava.local/capabilities</span>
            <span class="mode-badge" :data-mode="capabilities.mode">
              {{ capabilities.mode === 'native'
                ? t('capabilityReady')
                : capabilities.mode === 'fallback'
                  ? t('capabilityFallback')
                  : t('capabilityLimited') }}
            </span>
          </div>
          <CapabilityList :capabilities="capabilities" />
        </div>
      </div>
    </section>

    <section class="section section--mist comparison">
      <div class="container">
        <div class="section-heading">
          <p class="kicker">{{ t('comparisonKicker') }}</p>
          <h2>{{ t('comparisonTitle') }}</h2>
        </div>
        <div class="edition-grid">
          <article class="edition-card edition-card--active">
            <div class="edition-card__top">
              <span class="edition-icon">
                <svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="12" /><path d="M4 16h24M16 4a19 19 0 0 1 0 24M16 4a19 19 0 0 0 0 24" /></svg>
              </span>
              <span class="edition-badge">{{ t('currentEdition') }}</span>
            </div>
            <h3>{{ t('webEdition') }}</h3>
            <p>{{ t('webEditionBody') }}</p>
          </article>
          <article class="edition-card">
            <div class="edition-card__top">
              <span class="edition-icon">
                <svg viewBox="0 0 32 32"><rect x="3" y="5" width="26" height="18" rx="2" /><path d="M11 28h10M16 23v5" /></svg>
              </span>
            </div>
            <h3>{{ t('desktopEdition') }}</h3>
            <p>{{ t('desktopEditionBody') }}</p>
            <a href="https://github.com/sadrazkh/Storava" target="_blank" rel="noreferrer">
              {{ t('learnDesktop') }}
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
            </a>
          </article>
        </div>
      </div>
    </section>

    <section id="privacy" class="section privacy-section">
      <div class="privacy-section__pattern" aria-hidden="true" />
      <div class="container privacy-layout">
        <div class="privacy-visual" aria-hidden="true">
          <div class="privacy-visual__device">
            <span class="privacy-visual__camera" />
            <div class="privacy-visual__screen">
              <span /><span /><span />
              <svg viewBox="0 0 64 64">
                <path d="M32 6 54 16v14c0 14-9.2 23.4-22 28C19.2 53.4 10 44 10 30V16z" />
                <path d="m22 32 7 7 14-17" />
              </svg>
            </div>
          </div>
          <div class="privacy-visual__barrier"><i /><i /><i /></div>
        </div>
        <div>
          <p class="kicker">{{ t('privacyKicker') }}</p>
          <h2>{{ t('privacyTitle') }}</h2>
          <p class="section-lead">{{ t('privacyBody') }}</p>
          <ul class="privacy-points">
            <li><span>01</span>{{ t('privacyPointOne') }}</li>
            <li><span>02</span>{{ t('privacyPointTwo') }}</li>
            <li><span>03</span>{{ t('privacyPointThree') }}</li>
          </ul>
          <a class="text-link" href="/privacy">
            {{ t('privacyLink') }}
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
          </a>
        </div>
      </div>
    </section>

    <section class="final-cta">
      <div class="final-cta__rings" aria-hidden="true" />
      <div class="container final-cta__inner">
        <p class="kicker">{{ t('ctaKicker') }}</p>
        <h2>{{ t('ctaTitle') }}</h2>
        <p>{{ t('ctaBody') }}</p>
        <a class="button button--lime button--large" href="/scan">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 7h7l2 2h9v10H3z" /></svg>
          {{ t('startScan') }}
        </a>
      </div>
    </section>

    <footer class="site-footer">
      <div class="container site-footer__inner">
        <BrandMark />
        <p>{{ t('footerTagline') }}</p>
        <span>{{ t('footerPhase') }}</span>
      </div>
    </footer>

    <OnboardingDialog
      :open="onboardingOpen"
      :capabilities="capabilities"
      @close="onboardingOpen = false"
    />
  </div>
</template>
