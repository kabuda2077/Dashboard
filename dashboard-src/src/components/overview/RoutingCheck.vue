<template>
  <section class="network-test-page">
    <div
      v-if="!hideHeader"
      class="flex items-center justify-between"
    >
      <div
        class="text-base-content/60 text-xs font-semibold tracking-wider uppercase"
        :title="$t('networkRoutingHelp')"
      >
        {{ $t('networkRouting') }}
      </div>
      <button
        class="network-icon-button"
        type="button"
        :disabled="routingTargets.every((target) => target.checking.value) || !backendSessionReady"
        :aria-label="$t('networkTestRouting')"
        @click="getRouting()"
      >
        <BoltIcon
          class="h-3.5 w-3.5"
          :class="isTestingRouting ? 'animate-pulse' : ''"
        />
      </button>
    </div>
    <div class="network-test-grid network-routing-grid">
      <button
        v-for="target in routingTargets"
        :key="target.id"
        :data-routing-target="target.id"
        type="button"
        class="network-bento-tile"
        :disabled="target.checking.value || !backendSessionReady"
        :aria-label="$t('networkTestTarget', { name: $t(`networkTarget_${target.id}`) })"
        :aria-busy="target.checking.value"
        @click="getRouting([target.id])"
      >
        <div class="network-tile-top">
          <div class="network-tile-title-wrap">
            <span
              class="network-tile-dot"
              :class="routingDotColor(target.result.value)"
            />
            <span
              class="network-tile-name"
              :title="target.url"
            >
              {{ $t(`networkTarget_${target.id}`) }}
            </span>
          </div>
          <span class="network-tile-badge">
            {{ $t(target.region === 'domestic' ? 'networkDomestic' : 'networkInternational') }}
          </span>
        </div>
        <span
          v-if="target.checking.value && target.result.value.status === 'idle'"
          class="text-base-content/45 mt-2 flex h-5 items-center gap-2 text-xs"
          role="status"
        >
          <span class="loading loading-spinner loading-xs" />{{ $t('checking') }}
        </span>
        <span
          v-else
          class="network-tile-route-val"
          :class="
            target.result.value.status === 'observed' ? 'text-base-content' : 'text-base-content/45'
          "
          :title="
            target.result.value.status === 'observed'
              ? $t('networkRouteObserved')
              : resultText(target.result.value)
          "
          role="status"
        >
          {{ resultText(target.result.value) }}
        </span>
        <div
          class="network-tile-track relative"
          aria-hidden="true"
        >
          <div
            v-if="target.result.value.status === 'observed'"
            class="network-tile-track-fill bg-low-latency w-full"
          />
          <div
            v-if="target.checking.value"
            class="network-routing-activity"
          />
        </div>
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { backendSessionReady } from '@/helper/backendSession'
import { getRouting, isTestingRouting, routingTargets } from '@/helper/routing-test'
import { useNetworkDiagnosticsPage } from '@/helper/networkDiagnostics'
import { toRef } from 'vue'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { useI18n } from 'vue-i18n'

const props = withDefaults(defineProps<{ active?: boolean; hideHeader?: boolean }>(), {
  active: true,
  hideHeader: false,
})
useNetworkDiagnosticsPage('routing', toRef(props, 'active'))
const { t } = useI18n()
type RoutingResult = (typeof routingTargets)[number]['result']['value']

const routingDotColor = (result: RoutingResult) =>
  result.status === 'observed' ? 'bg-low-latency' : 'bg-base-content/20'
const resultText = (result: RoutingResult) => {
  if (result.status === 'observed')
    return result.direct ? `🇨🇳 ${t('direct')}` : (result.node ?? t('networkRouteUnconfirmed'))
  if (result.status === 'unavailable') {
    if (result.reason === 'proxyAuthenticationRequired') return t('networkRouteProxyAuthentication')
    if (result.reason === 'hostRequired') return t('networkRouteHostRequired')
    if (result.reason === 'noProxyInbound') return t('networkRouteNoInbound')
    return t('networkRouteUnconfirmed')
  }
  if (result.status === 'failed') return t('networkRequestFailed')
  if (result.status === 'idle') return t('networkNotTested')
  return t('networkRouteUnconfirmed')
}
</script>

<style scoped>
.network-routing-activity {
  position: absolute;
  top: 0;
  left: 0;
  width: 35%;
  height: 100%;
  border-radius: inherit;
  background: color-mix(in srgb, var(--color-base-content) 25%, transparent);
  transform: translateX(93%);
  animation: network-routing-scan 1.3s ease-in-out infinite;
}
@keyframes network-routing-scan {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: translateX(286%);
  }
}
@media (prefers-reduced-motion: reduce) {
  .network-routing-activity {
    animation: none;
  }
}
</style>
