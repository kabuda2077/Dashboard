import type { CoreKind, CoreProfile, CoreProfileDraft } from '@/composables/hostBridge'
import { reactive } from 'vue'

export type DraftValues = Pick<CoreProfile, 'exePath' | 'configPath' | 'apiUrl' | 'secret'>
export type CoreDraft = {
  base: CoreProfile
  values: DraftValues
  replaceSecret: boolean
  remote?: CoreProfile
}
const keys = ['exePath', 'configPath', 'apiUrl', 'secret'] as const
const valuesOf = (profile: CoreProfile): DraftValues => ({
  exePath: profile.exePath,
  configPath: profile.configPath,
  apiUrl: profile.apiUrl,
  secret: profile.secret,
})
export const coreDrafts = reactive<Partial<Record<CoreKind, CoreDraft>>>({})
export const isDraftDirty = (draft: CoreDraft) =>
  draft.replaceSecret || keys.some((key) => draft.values[key] !== draft.base[key])
export const syncCoreDrafts = (profiles: Record<CoreKind, CoreProfile>) => {
  for (const kind of ['mihomo', 'sing-box'] as const) {
    const profile = profiles[kind]
    const draft = coreDrafts[kind]
    if (!draft)
      coreDrafts[kind] = { base: { ...profile }, values: valuesOf(profile), replaceSecret: false }
    else if (!isDraftDirty(draft)) {
      draft.base = { ...profile }
      Object.assign(draft.values, valuesOf(profile))
      draft.replaceSecret = false
      draft.remote = undefined
    } else if (profile.revision !== draft.base.revision) draft.remote = { ...profile }
  }
}
export const resetCoreDraft = (kind: CoreKind, profile: CoreProfile) => {
  coreDrafts[kind] = { base: { ...profile }, values: valuesOf(profile), replaceSecret: false }
}
export const toProfileEdit = (draft: CoreDraft): CoreProfileDraft => ({
  exePath: draft.values.exePath,
  configPath: draft.values.configPath,
  apiUrl: draft.values.apiUrl,
  secret: draft.base.secretDecryptionFailed
    ? draft.replaceSecret
      ? { action: 'replace', value: draft.values.secret }
      : { action: 'keep' }
    : draft.values.secret !== draft.base.secret
      ? { action: 'replace', value: draft.values.secret }
      : { action: 'keep' },
})
export const acceptCoreDraft = (
  draft: CoreDraft,
  submitted: DraftValues,
  canonical: CoreProfile,
) => {
  for (const key of keys)
    if (draft.values[key] === submitted[key]) draft.values[key] = canonical[key]
  if (draft.values.secret === canonical.secret) draft.replaceSecret = false
  draft.base = { ...canonical }
  draft.remote = undefined
}
