import { disconnectAllAPI, disconnectByIdAPI } from '@/assembly/connections'
import {
  CONNECTION_CARD_GROUPABLE_KEYS,
  connectionCardGroupKey,
  hasConnectionCardGroups,
  hasExpandedConnectionCardGroups,
  toggleAllConnectionCardGroups,
  type ConnectionCardGroupKey,
} from '@/composables/connectionCardGroups'
import { useCtrlsBar } from '@/composables/useCtrlsBar'
import { ROUTE_NAME, SETTINGS_MENU_KEY, SORT_DIRECTION, SORT_TYPE } from '@/constant'
import { useTooltip } from '@/helper/tooltip'
import { runManualRequest } from '@/helper/requestError'
import {
  connectionFilter,
  connections,
  connectionSortDirection,
  connectionSortType,
  isPaused,
  quickFilterEnabled,
  quickFilterRegex,
  renderConnections,
} from '@/store/connections'
import { isConnectionCard } from '@/store/settings'
import {
  BarsArrowDownIcon,
  BarsArrowUpIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  LinkIcon,
  LinkSlashIcon,
  PauseIcon,
  PlayIcon,
  QuestionMarkCircleIcon,
  WrenchScrewdriverIcon,
  XMarkIcon,
} from '@heroicons/vue/24/outline'
import { defineComponent, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import CtrlsBar from '../common/CtrlsBar.vue'
import DialogWrapper from '../common/DialogWrapper.vue'
import DropdownSelect from '../common/DropdownSelect.vue'
import TextInput from '../common/TextInput.vue'
import ConnectionCardSettings from '../settings/connections/ConnectionCardSettings.vue'
import TableSettings from '../settings/connections/TableSettings.vue'
import ConnectionTabs from './ConnectionTabs.vue'
import SourceIPFilter from './SourceIPFilter.vue'

const handlerClickCloseAll = () => {
  if (renderConnections.value.length === connections.value.length) {
    void runManualRequest(() => disconnectAllAPI())
  } else {
    renderConnections.value.forEach((conn) => {
      void runManualRequest(() => disconnectByIdAPI(conn.id))
    })
  }
}

export default defineComponent({
  name: 'ConnectionCtrl',
  components: {
    TextInput,
    ConnectionTabs,
    SourceIPFilter,
  },
  setup() {
    const { t } = useI18n()
    const router = useRouter()
    const settingsModel = ref(false)
    const { showTip, updateTip } = useTooltip()
    const { isLargeCtrlsBar } = useCtrlsBar(() => (isConnectionCard.value ? 860 : 720))

    return () => {
      const sortForCards = (
        <div class={`flex flex-1 items-center gap-2 ${isLargeCtrlsBar.value ? 'min-w-46' : ''}`}>
          <DropdownSelect
            class="min-w-32 flex-1"
            modelValue={connectionSortType.value}
            onUpdate:modelValue={(value) => (connectionSortType.value = value as SORT_TYPE)}
            options={(Object.values(SORT_TYPE) as string[]).map((opt) => ({
              label: t(opt) || opt,
              value: opt,
            }))}
          />
          <button
            class="btn btn-sm"
            onClick={() => {
              connectionSortDirection.value =
                connectionSortDirection.value === SORT_DIRECTION.ASC
                  ? SORT_DIRECTION.DESC
                  : SORT_DIRECTION.ASC
            }}
          >
            {connectionSortDirection.value === SORT_DIRECTION.ASC ? (
              <BarsArrowUpIcon class="h-4 w-4" />
            ) : (
              <BarsArrowDownIcon class="h-4 w-4" />
            )}
          </button>
        </div>
      )

      const groupForCards = (
        <DropdownSelect
          class="min-w-32 flex-1"
          modelValue={connectionCardGroupKey.value}
          onUpdate:modelValue={(value) =>
            (connectionCardGroupKey.value = value as ConnectionCardGroupKey | null)
          }
          options={[
            { value: null, label: t('noGrouping') },
            ...CONNECTION_CARD_GROUPABLE_KEYS.map((value) => ({ value, label: t(value) })),
          ]}
        />
      )

      const toggleGroupsLabel = () =>
        hasExpandedConnectionCardGroups.value ? t('collapseAllGroups') : t('expandAllGroups')
      const toggleGroupsButton =
        isConnectionCard.value && connectionCardGroupKey.value !== null ? (
          <button
            class="btn btn-circle btn-sm"
            disabled={!hasConnectionCardGroups.value}
            aria-label={toggleGroupsLabel()}
            onClick={() => {
              toggleAllConnectionCardGroups()
              updateTip(toggleGroupsLabel())
            }}
            onMouseenter={(e) => showTip(e, toggleGroupsLabel(), { appendTo: 'parent' })}
          >
            {hasExpandedConnectionCardGroups.value ? (
              <ChevronUpIcon class="h-4 w-4" />
            ) : (
              <ChevronDownIcon class="h-4 w-4" />
            )}
          </button>
        ) : null

      const settingsModal = (
        <>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => (settingsModel.value = true)}
          >
            <WrenchScrewdriverIcon class="h-4 w-4" />
          </button>
          <DialogWrapper
            v-model={settingsModel.value}
            title={t('connectionSettings')}
          >
            <div class="flex flex-col gap-3 text-sm">
              <div class="settings-grid">
                {isConnectionCard.value && (
                  <div class="setting-item">
                    <div class="setting-item-label">{t('groupBy')}</div>
                    {groupForCards}
                  </div>
                )}
                <div class="setting-item">
                  <div class="setting-item-label shrink-0!">{t('hideConnectionRegex')}</div>
                  <TextInput
                    class="w-32 max-w-64 flex-1"
                    v-model={quickFilterRegex.value}
                  />
                </div>
                <div class="setting-item">
                  <div class="setting-item-label flex items-center gap-2">
                    <span>{t('hideConnection')}</span>
                    <div
                      onMouseenter={(e) =>
                        showTip(e, t('hideConnectionTip'), {
                          appendTo: 'parent',
                        })
                      }
                    >
                      <QuestionMarkCircleIcon class="h-4 w-4" />
                    </div>
                  </div>
                  <input
                    type="checkbox"
                    class="toggle"
                    v-model={quickFilterEnabled.value}
                  />
                </div>
                {isConnectionCard.value ? <ConnectionCardSettings /> : <TableSettings />}
              </div>
              <div class="divider m-0"></div>
              <button
                class="btn btn-block"
                onClick={() => {
                  settingsModel.value = false
                  router.push({
                    name: ROUTE_NAME.core,
                    query: { scrollTo: SETTINGS_MENU_KEY.connections },
                  })
                }}
              >
                {t('moreSettings')}
              </button>
            </div>
          </DialogWrapper>
        </>
      )

      const searchInput = (
        <TextInput
          v-model={connectionFilter.value}
          placeholder={`${t('search')} | Regex`}
          clearable={true}
          class={isLargeCtrlsBar.value ? 'ctrls-search min-w-0' : 'w-full'}
        />
      )

      const buttons = (
        <>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => {
              quickFilterEnabled.value = !quickFilterEnabled.value
              updateTip(quickFilterEnabled.value ? t('showConnection') : t('hideConnection'))
            }}
            onMouseenter={(e) =>
              showTip(e, quickFilterEnabled.value ? t('showConnection') : t('hideConnection'), {
                appendTo: 'parent',
              })
            }
          >
            {quickFilterEnabled.value ? (
              <LinkSlashIcon class="h-4 w-4" />
            ) : (
              <LinkIcon class="h-4 w-4" />
            )}
          </button>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => {
              isPaused.value = !isPaused.value
            }}
          >
            {isPaused.value ? <PlayIcon class="h-4 w-4" /> : <PauseIcon class="h-4 w-4" />}
          </button>
          <button
            class="btn btn-circle btn-sm"
            onClick={handlerClickCloseAll}
          >
            <XMarkIcon class="h-4 w-4" />
          </button>
        </>
      )

      const content = !isLargeCtrlsBar.value ? (
        <div class="flex flex-wrap items-center gap-2 p-2">
          <div class="flex w-full items-center justify-between gap-2">
            <ConnectionTabs />
            {!isConnectionCard.value && (
              <div class="flex items-center gap-1">
                {settingsModal}
                {buttons}
              </div>
            )}
          </div>
          {isConnectionCard.value && (
            <div class="flex w-full items-center gap-2">
              {sortForCards}
              {toggleGroupsButton}
              {settingsModal}
              {buttons}
            </div>
          )}
          <div class="flex w-full items-center gap-2">
            <SourceIPFilter class="w-40" />
            {searchInput}
          </div>
        </div>
      ) : (
        <div class="flex items-center gap-2 p-2">
          <ConnectionTabs />
          {isConnectionCard.value && sortForCards}
          {toggleGroupsButton}
          <SourceIPFilter class="w-40" />
          {searchInput}
          {settingsModal}
          {buttons}
        </div>
      )

      return <CtrlsBar>{content}</CtrlsBar>
    }
  },
})
