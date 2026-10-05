import { beforeEach, expect, it, vi } from 'vitest'
import { makeHostSnapshot } from './hostFixture'
beforeEach(() => vi.resetModules())
it('unreadable current-format credentials require an explicit replacement, including empty', async () => {
  const { coreDrafts, syncCoreDrafts, toProfileEdit } = await import('@/helper/hostDraft')
  syncCoreDrafts(makeHostSnapshot({ secretDecryptionFailed: true }).profiles)
  const draft = coreDrafts.mihomo!
  draft.values.exePath = 'C:/different.exe'
  expect(toProfileEdit(draft).secret).toEqual({ action: 'keep' })
  draft.values.secret = 'new'
  expect(toProfileEdit(draft).secret).toEqual({ action: 'keep' })
  draft.replaceSecret = true
  expect(toProfileEdit(draft).secret).toEqual({ action: 'replace', value: 'new' })
  draft.values.secret = ''
  expect(toProfileEdit(draft).secret).toEqual({ action: 'replace', value: '' })
})
