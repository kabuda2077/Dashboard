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
import { hostIconCache } from '@/composables/hostBridge'
import DOMPurify from 'dompurify'
import { computed } from 'vue'

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

const style = computed(() => {
  return {
    width: `${props.size}px`,
    height: `${props.size}px`,
    marginRight: `${props.margin}px`,
  }
})
const DOM_STARTS_WITH = 'data:image/svg+xml,'

const resolveCachedIcon = (icon: string) => {
  if (!icon) return icon

  const cachedIcon = hostIconCache.value[icon]
  if (cachedIcon) return cachedIcon

  try {
    const href = new URL(icon).href
    return hostIconCache.value[href] || icon
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
</script>
