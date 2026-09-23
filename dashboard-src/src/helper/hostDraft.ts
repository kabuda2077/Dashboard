// Reconcile host snapshots without replacing fields edited since the previous snapshot.
export const mergeHostDraft = <T extends Record<string, unknown>>(
  draft: T, previous: Partial<T>, incoming: T,
): T => {
  const merged = { ...incoming }
  for (const key of Object.keys(incoming) as (keyof T)[]) {
    if (Object.hasOwn(previous, key) && draft[key] !== previous[key]) merged[key] = draft[key]
  }
  return merged
}
