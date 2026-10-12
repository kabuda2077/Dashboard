import { renderRoutes } from '@/helper'
import { isMiddleScreen } from '@/helper/utils'
import { computed, ref } from 'vue'
import type { RouteLocationNormalized } from 'vue-router'

export type SlideDirection = 'slide-left' | 'slide-right' | ''
const slideDirection = ref<SlideDirection>('')
let pendingSwipeDirection: SlideDirection = ''

export const setPendingSwipeDirection = (direction: SlideDirection) => {
  pendingSwipeDirection = direction
}
export const pageTransitionName = computed(() =>
  isMiddleScreen.value ? slideDirection.value : 'page',
)
export const pageTransitionMode = computed(() =>
  isMiddleScreen.value ? undefined : ('out-in' as const),
)

export const resolvePageTransition = (to: RouteLocationNormalized, from: RouteLocationNormalized) => {
  const swipeDirection = pendingSwipeDirection
  pendingSwipeDirection = ''
  const routes = renderRoutes.value
  const toIndex = routes.findIndex((item) => item === to.name)
  const fromIndex = routes.findIndex((item) => item === from.name)
  if (to.name === from.name || toIndex < 0 || fromIndex < 0) {
    slideDirection.value = ''
    return
  }
  const lastIndex = routes.length - 1
  slideDirection.value = swipeDirection ||
    (toIndex === 0 && fromIndex === lastIndex ? 'slide-left' :
      toIndex === lastIndex && fromIndex === 0 ? 'slide-right' :
        toIndex < fromIndex ? 'slide-right' : 'slide-left')
}
