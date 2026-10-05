import { afterEach, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref } from 'vue'
import { createI18n } from 'vue-i18n'
import EditBackend from '@/components/settings/backend/EditBackendModal.vue'
import { isBackendAvailable } from '@/assembly/backend'
import { updateBackend } from '@/store/setup'
vi.mock('@/assembly/backend', () => ({ isBackendAvailable: vi.fn() }))
vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))
vi.mock('@/components/common/DialogWrapper.vue', () => ({ default: defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) }) }))
vi.mock('@/store/setup', () => ({
  activeBackend: ref({ uuid:'a' }),
  backendList: ref(['a','b'].map(uuid => ({ uuid, type:'clash', protocol:'http', host:uuid+'.example', port:'9090', password:'', label:uuid }))),
  updateBackend: vi.fn(),
}))
afterEach(() => vi.clearAllMocks())
it.each(['same','slot','draft','closed'])('late browser probe respects the original editor context (%s)', async scenario => {
  let finish!: (value:boolean) => void
  vi.mocked(isBackendAvailable).mockImplementation(() => new Promise(resolve => { finish=resolve }))
  const visible=ref(true)
  const root=document.createElement('div'); root.id='app-content'; document.body.append(root)
  const app=createApp({render:()=>h(EditBackend,{modelValue:visible.value,'onUpdate:modelValue':(value:boolean)=>{visible.value=value}})})
  app.use(createI18n({legacy:false,locale:'en',messages:{en:{}},missingWarn:false,fallbackWarn:false}))
  app.mount(root)
  try {
    await nextTick()
    const save=[...root.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent?.trim()==='save')!
    save.click(); await nextTick()
    expect(isBackendAvailable).toHaveBeenCalledOnce()
    if(scenario==='slot') { const select=root.querySelector('select')!; select.value='b'; select.dispatchEvent(new Event('change',{bubbles:true})) }
    if(scenario==='draft') { const input=root.querySelector('input')!; input.value='new-edit.example'; input.dispatchEvent(new Event('input',{bubbles:true})) }
    if(scenario==='closed') visible.value=false
    await nextTick()
    finish(true)
    await nextTick(); await nextTick(); await nextTick()
    if(scenario==='same') expect(updateBackend).toHaveBeenCalledWith('a',expect.objectContaining({host:'a.example'}))
    else expect(updateBackend).not.toHaveBeenCalled()
    if(scenario==='draft') expect(root.querySelector('input')!.value).toBe('new-edit.example')
  } finally {app.unmount();root.remove()}
})
