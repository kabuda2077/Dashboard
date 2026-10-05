import { expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, ref } from 'vue'
import { createI18n } from 'vue-i18n'
import Card from '@/components/connections/ConnectionCard'
import Details from '@/components/connections/ConnectionDetails.vue'
import { getConnectionDisplayValue } from '@/assembly/connections'
import { getConnectionSmartBlock } from '@/helper'

vi.mock('@/assembly/connections', () => ({
  disconnectByIdAPI: vi.fn(), blockConnectionByIdAPI: vi.fn(),
  getConnectionDisplayValue: vi.fn((conn, key, options) => key === 'chains' && options.showFullProxyChain ? conn.chains.join(' → ') : key),
}))
vi.mock('@/assembly/proxies', () => ({ proxyMap: ref({}) }))
vi.mock('@/api/geoip', () => ({ getIPInfo: vi.fn() }))
vi.mock('@/helper/requestError', () => ({ runManualRequest: vi.fn() }))
vi.mock('@/composables/bouncein', () => ({ useBounceOnVisible: vi.fn() }))
vi.mock('@/composables/connections', () => ({ useConnections: () => ({
  handlerInfo: vi.fn(), infoConn: ref({id:'fixture',chains:['Node','Middle','Group']}), connectionDetailModalShow:ref(true),
}) }))
vi.mock('@/helper', () => ({
  getConnectionChains: (conn: {chains:string[]}) => conn.chains,
  getConnectionSmartBlock: vi.fn(), getConnectionSourceIP: () => '', getDestinationFromConnection: () => undefined,
}))
vi.mock('@/store/connections', () => ({ connectionFilter:ref(''), connectionTabShow:ref('active'), isClosedConnection:()=>false }))
vi.mock('@/store/settings', () => ({ connectionCardLines:ref([['host']]), proxyChainDirection:ref('normal'), showFullProxyChain:ref(false) }))
vi.mock('@/components/common/DialogWrapper.vue', () => ({ default: defineComponent({setup:(_, {slots})=>()=>h('div',slots.default?.())}) }))
vi.mock('@/components/common/ProxyChainPath.vue', () => ({ default:{render:()=>null} }))
vi.mock('@/components/proxies/ProxyGroupPanel.vue', () => ({ default:{render:()=>null} }))
vi.mock('@/components/proxies/ProxyName.vue', () => ({ default:{render:()=>null} }))
vi.mock('@/components/proxies/ProxyIcon.vue', () => ({ default:{render:()=>null} }))
vi.mock('@/components/settings/connections/SourceIPLabels.vue', () => ({ default:{render:()=>null} }))

it('a card computes only selected fields, not unused GeoIP and action builders', () => {
  vi.mocked(getConnectionDisplayValue).mockClear()
  const root=document.createElement('div')
  const app=createApp(Card,{conn:{id:'fixture',chains:[]} as never})
  app.mount(root)
  try {
    expect(getConnectionDisplayValue).toHaveBeenCalledTimes(1)
    expect(vi.mocked(getConnectionDisplayValue).mock.calls[0][1]).toBe('host')
    expect(getConnectionSmartBlock).not.toHaveBeenCalled()
  } finally {app.unmount()}
})
it('details always show the full chain even when the list setting hides middle nodes', () => {
  const root=document.createElement('div');root.id='app-content';document.body.append(root)
  const app=createApp(Details)
  app.use(createI18n({legacy:false,locale:'en',messages:{en:{}},missingWarn:false,fallbackWarn:false}))
  app.mount(root)
  try {
    expect(root.textContent).toContain('Node → Middle → Group')
    expect([...root.querySelectorAll('[role=tab]')].every(node=>node.tagName==='BUTTON')).toBe(true)
    expect(root.querySelector('[aria-selected=true]')).not.toBeNull()
  } finally {app.unmount();root.remove()}
})
