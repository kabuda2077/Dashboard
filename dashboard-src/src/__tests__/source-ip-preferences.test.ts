import { applyDashboardSettingsToStorage, parseSettingsDocument } from '@/helper/utils'
import { sourceIPLabelList } from '@/store/settings'
import { addBackend, backendList, removeBackend } from '@/store/setup'
import { afterEach, expect, it } from 'vitest'

afterEach(() => localStorage.clear())

it('rejects invalid source regex imports before replacing any stored preferences', () => {
  localStorage.setItem('config/theme', 'light')
  const invalid = {
    'config/theme': 'dark',
    'config/source-ip-label-list': JSON.stringify([{ id: 'broken', key: '/[', label: 'broken' }]),
  }
  expect(() => parseSettingsDocument({ schemaVersion: 2, preferences: invalid })).toThrow(
    'config/source-ip-label-list',
  )
  expect(() => applyDashboardSettingsToStorage(invalid, true)).toThrow('config/source-ip-label-list')
  expect(localStorage.getItem('config/theme')).toBe('light')
  expect(localStorage.getItem('config/source-ip-label-list')).toBeNull()
  const valid = {
    'config/source-ip-label-list': JSON.stringify([
      { id: 'regex', key: '/^192', label: 'regex', scope: ['local'] },
      { id: 'cidr', key: 'fe80::/10', label: 'CIDR' },
    ]),
  }
  expect(parseSettingsDocument({ schemaVersion: 2, preferences: valid })).toEqual(valid)
  applyDashboardSettingsToStorage(valid)
  expect(localStorage.getItem('config/source-ip-label-list')).toBe(valid['config/source-ip-label-list'])
})

it('deletes exclusive labels with a removed browser backend and retains shared and global labels', () => {
  backendList.value = []
  for (const host of ['a.example', 'b.example']) {
    addBackend({ type: 'clash', protocol: 'http', host, port: '9090', secondaryPath: '', password: '' })
  }
  const [a, b] = backendList.value.map((backend) => backend.uuid)
  sourceIPLabelList.value = [
    { id: 'exclusive', key: '192.0.2.1', label: 'exclusive', scope: [a] },
    { id: 'shared', key: '192.0.2.2', label: 'shared', scope: [a, b] },
    { id: 'other', key: '192.0.2.3', label: 'other', scope: [b] },
    { id: 'global', key: '192.0.2.4', label: 'global' },
  ]
  removeBackend(a)
  expect(sourceIPLabelList.value).toEqual([
    { id: 'shared', key: '192.0.2.2', label: 'shared', scope: [b] },
    { id: 'other', key: '192.0.2.3', label: 'other', scope: [b] },
    { id: 'global', key: '192.0.2.4', label: 'global' },
  ])
})
