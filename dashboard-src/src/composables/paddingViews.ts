import { isMiddleScreen } from '@/helper/utils'
import { computed, ref } from 'vue'

export const ctrlsBottom = ref(0)
export const dockTop = ref(0)
export const usePaddingForViews = (
  config = {
    offsetTop: 8,
    offsetBottom: 8,
  },
) => {
  const { offsetTop, offsetBottom } = config
  const paddingTop = computed(() => {
    return ctrlsBottom.value ? ctrlsBottom.value + offsetTop : 0
  })
  const paddingBottom = computed(() => {
    if (isMiddleScreen.value) {
      return dockTop.value + offsetBottom
    }
    return 0
  })

  const padding = computed(() => {
    const nextPadding: Record<string, string> = {}

    if (paddingTop.value) {
      nextPadding.paddingTop = `${paddingTop.value}px`
    }

    if (paddingBottom.value) {
      nextPadding.paddingBottom = `${paddingBottom.value}px`
    }

    return nextPadding
  })

  return {
    padding,
    paddingTop,
    paddingBottom,
  }
}
