import { describe, expect, it } from 'vitest'
import { getAppUpdateFeedback } from '@/components/settings/general/appUpdateFeedback'

describe('structured app update feedback', () => {
  it.each([
    [{ type: 'appUpdateResult', result: 'available', manual: true, latestVersion: '1.3.0' }, '发现 v1.3.0'],
    [{ type: 'appUpdateResult', result: 'upToDate', manual: true, currentVersion: '1.2.0' }, '已是最新版本 v1.2.0'],
    [{ type: 'appUpdateResult', result: 'failed', manual: true }, '检查失败，请稍后重试'],
    [{ type: 'appUpdateResult', result: 'busy', manual: true }, '正在检查，请稍候'],
  ] as const)('maps %s without parsing notice text', (message, expected) => {
    expect(getAppUpdateFeedback(message)).toBe(expected)
  })

  it('ignores automatic results and ordinary notices', () => {
    expect(getAppUpdateFeedback({
      type: 'appUpdateResult',
      result: 'available',
      manual: false,
      latestVersion: '1.3.0',
    })).toBeUndefined()
    expect(getAppUpdateFeedback({
      type: 'notice',
      message: '发现 Dashboard 新版本 v9.9.9',
    })).toBeUndefined()
  })
})
