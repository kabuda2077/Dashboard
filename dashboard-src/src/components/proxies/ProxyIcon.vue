<template>
  <div
    v-if="isDom"
    :class="['inline-block', fill || 'fill-primary']"
    :style="style"
    v-html="pureDom"
  />
  <img
    v-else
    class="inline-block"
    :style="style"
    :src="resolvedIcon"
  />
</template>

<script setup lang="ts">
import DOMPurify from 'dompurify'
import { computed, onMounted, onUnmounted, ref } from 'vue'

type HostWindow = Window & {
  __mihomoIconCache?: Record<string, string>
}

const props = withDefaults(
  defineProps<{
    icon: string
    fill?: string
    size?: number
    margin?: number
  }>(),
  {
    size: 16,
    margin: 4,
  },
)

const cacheVersion = ref(0)
const style = computed(() => {
  return {
    width: `${props.size}px`,
    height: `${props.size}px`,
    marginRight: `${props.margin}px`,
  }
})
const DOM_STARTS_WITH = 'data:image/svg+xml,'

const resolveCachedIcon = (icon: string) => {
  cacheVersion.value
  const cache = (window as HostWindow).__mihomoIconCache
  if (!cache || !icon) return icon

  const cachedIcon = cache[icon]
  if (cachedIcon) return cachedIcon

  try {
    const href = new URL(icon).href
    return cache[href] || icon
  } catch {
    return icon
  }
}

const resolvedIcon = computed(() => resolveCachedIcon(props.icon))
const isDom = computed(() => {
  return resolvedIcon.value.startsWith(DOM_STARTS_WITH)
})

const pureDom = computed(() => {
  if (!isDom.value) return
  return DOMPurify.sanitize(resolvedIcon.value.replace(DOM_STARTS_WITH, ''))
})

const updateIconCache = () => {
  cacheVersion.value++
}

onMounted(() => {
  window.addEventListener('__mihomoIconCacheUpdated', updateIconCache)
})

onUnmounted(() => {
  window.removeEventListener('__mihomoIconCacheUpdated', updateIconCache)
})
</script>
