<template>
  <section class="network-test-page">
    <div class="flex items-center justify-between">
      <div
        class="text-base-content/60 text-xs font-semibold tracking-wider uppercase"
        :title="$t('networkRoutingHelp')"
      >
        {{ $t('networkRouting') }}
      </div>
      <button
        class="btn btn-ghost btn-xs btn-circle"
        type="button"
        :disabled="isTestingRouting || !backendSessionReady"
        :aria-label="$t('networkTestRouting')"
        @click="getRouting"
      >
        <BoltIcon
          class="h-3.5 w-3.5"
          :class="isTestingRouting ? 'animate-pulse' : ''"
        />
      </button>
    </div>
    <div class="network-test-grid network-routing-grid">
      <div
        v-for="target in routingTargets"
        :key="target.id"
        class="flex min-w-0 flex-col justify-center gap-2"
        :data-routing-target="target.id"
      >
        <div class="flex min-w-0 items-center gap-2">
          <span
            class="min-w-0 truncate text-sm font-medium"
            :title="target.url"
          >
            {{ $t(`networkTarget_${target.id}`) }}
          </span>
          <span class="text-base-content/45 shrink-0 text-[10px]">
            {{ $t(target.region === 'domestic' ? 'networkDomestic' : 'networkInternational') }}
          </span>
        </div>
        <span
          v-if="target.result.value.status === 'checking'"
          class="text-base-content/45 flex h-5 items-center gap-2 text-xs"
          role="status"
        >
          <span class="loading loading-spinner loading-xs" />{{ $t('checking') }}
        </span>
        <span
          v-else
          class="truncate text-sm"
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
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { backendSessionReady } from '@/helper/backendSession'
import { getRouting, isTestingRouting, routingTargets } from '@/helper/routing-test'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
type RoutingResult = (typeof routingTargets)[number]['result']['value']
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
