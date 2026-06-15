import type { InjectionKey, Ref } from 'vue'

export type CoreHostActions = {
  isRunning: Readonly<Ref<boolean>>
  isCoreUpgrading: Readonly<Ref<boolean>>
  restartCore: () => void
  upgradeCore: () => void
}

export const coreHostActionsKey = Symbol('coreHostActions') as InjectionKey<CoreHostActions>
