import { hiddenSettingsItems } from '@/store/settings'
import type { MaybeRef } from 'vue'
import { computed, unref } from 'vue'

/**
 * Returns a computed that is true when the setting item with the given key is visible.
 * Use in templates for reactive visibility checks.
 */
export function useIsSettingVisible(key: MaybeRef<string>) {
  return computed(() => !hiddenSettingsItems.value[unref(key)])
}

/**
 * Returns a computed that is true when at least one of the given setting keys is visible.
 * Use for "has any visible item" in a settings section.
 */
export function useHasAnyVisibleSetting(keys: MaybeRef<string[]>) {
  return computed(() => unref(keys).some((k) => !hiddenSettingsItems.value[k]))
}
