import { isSingBoxCore } from '@/assembly/version'
import { useCtrlsBar } from '@/composables/useCtrlsBar'
import { LIST_DISPLAY_STYLE, LOG_LEVEL } from '@/constant'
import { useTooltip } from '@/helper/tooltip'
import {
  initLogs,
  isPaused,
  logFilter,
  logFilterEnabled,
  logFilterRegex,
  logLevel,
  logTypeFilter,
  logs,
} from '@/store/logs'
import { logDisplayStyle, logRetentionLimit, logSearchHistory } from '@/store/settings'
import {
  ArrowDownTrayIcon,
  LinkIcon,
  LinkSlashIcon,
  PauseIcon,
  PlayIcon,
  QuestionMarkCircleIcon,
  WrenchScrewdriverIcon,
  XMarkIcon,
} from '@heroicons/vue/24/outline'
import dayjs from 'dayjs'
import { debounce } from 'lodash'
import { computed, defineComponent, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import CtrlsBar from '../common/CtrlsBar.vue'
import DialogWrapper from '../common/DialogWrapper.vue'
import DropdownSelect from '../common/DropdownSelect.vue'
import TextInput from '../common/TextInput.vue'

export default defineComponent({
  setup() {
    const { t } = useI18n()
    const settingsModel = ref(false)
    const { isLargeCtrlsBar } = useCtrlsBar()
    const { showTip, updateTip } = useTooltip()
    const insertLogSearchHistory = debounce((log: string) => {
      if (!log) {
        return
      }

      const idx = logSearchHistory.value.indexOf(log)

      if (idx !== -1) {
        logSearchHistory.value.splice(idx, 1)
      }

      logSearchHistory.value.unshift(log)
      if (logSearchHistory.value.length > 5) {
        logSearchHistory.value.pop()
      }
    }, 1500)

    watch(logFilter, insertLogSearchHistory)

    const logLevels = computed(() => {
      if (isSingBoxCore.value) {
        return Object.values(LOG_LEVEL)
      }
      return [LOG_LEVEL.Debug, LOG_LEVEL.Info, LOG_LEVEL.Warning, LOG_LEVEL.Error, LOG_LEVEL.Silent]
    })

    const logFilterOptions = computed(() => {
      const types: string[] = []
      const levels: string[] = []

      if (isSingBoxCore.value) {
        for (const log of logs.value) {
          const startIndex = log.payload.startsWith('[') ? log.payload.indexOf(']') + 2 : 0
          const endIndex = log.payload.indexOf(':', startIndex)
          const type = log.payload.slice(startIndex, endIndex + 1)

          if (!types.includes(type)) {
            types.push(type)
          }

          if (!levels.includes(log.type)) {
            levels.push(log.type)
          }
        }
      } else {
        for (const log of logs.value) {
          const index = log.payload.indexOf(' ')
          const type = index === -1 ? log.payload : log.payload.slice(0, index)

          if (!types.includes(type)) {
            types.push(type)
          }

          if (!levels.includes(log.type)) {
            levels.push(log.type)
          }
        }
      }

      return {
        levels: levels.sort((a, b) => {
          const aIdx = logLevels.value.indexOf(a as LOG_LEVEL)
          const bIdx = logLevels.value.indexOf(b as LOG_LEVEL)
          return aIdx - bIdx
        }),
        types: types.sort(),
      }
    })

    const downloadAllLogs = () => {
      const blob = new Blob(
        [
          logs.value
            .map((log) =>
              [
                log.seq.toString().padEnd(5, ' '),
                log.time,
                log.type.padEnd(7, ' '),
                log.payload,
              ].join('\t'),
            )
            .join('\n'),
        ],
        {
          type: 'text/plain',
        },
      )
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = dayjs().format('YYYY-MM-DD HH-mm-ss') + '.log'
      a.click()
      URL.revokeObjectURL(url)
    }

    return () => {
      const levelSelect = (
        <DropdownSelect
          class="min-w-30 shrink-0"
          modelValue={logLevel.value}
          onUpdate:modelValue={(value) => {
            logLevel.value = value as LOG_LEVEL
            initLogs()
          }}
          options={logLevels.value.map((opt) => ({
            label: opt,
            value: opt,
          }))}
        />
      )
      const searchInput = (
        <TextInput
          v-model={logFilter.value}
          beforeClose={true}
          class="ctrls-search min-w-0"
          placeholder={`${t('search')} | Regex`}
          clearable={true}
          menus={logSearchHistory.value}
          menusDeleteable={true}
          onUpdate:menus={(val) => (logSearchHistory.value = val)}
        />
      )

      const logTypeSelect = (
        <DropdownSelect
          class={isLargeCtrlsBar.value ? 'w-36 shrink-0' : 'max-w-40 min-w-24 flex-1'}
          modelValue={logTypeFilter.value}
          onUpdate:modelValue={(value) => (logTypeFilter.value = value as string)}
          options={[
            { label: t('all'), value: '' },
            ...logFilterOptions.value.levels.map((opt) => ({
              label: opt,
              key: `level-${opt}`,
              value: opt,
            })),
            ...logFilterOptions.value.types.map((opt) => ({
              label: opt,
              key: `type-${opt}`,
              value: opt,
            })),
          ]}
        />
      )

      const settingsModal = (
        <>
          <button
            class={'btn btn-circle btn-sm'}
            onClick={() => (settingsModel.value = true)}
          >
            <WrenchScrewdriverIcon class="h-4 w-4" />
          </button>
          <DialogWrapper
            v-model={settingsModel.value}
            title={t('logSettings')}
          >
            <div class="flex flex-col gap-3 text-sm">
              <div class="settings-grid">
                <div class="setting-item">
                  <div class="setting-item-label">{t('logRetentionLimit')}</div>
                  <input
                    class="input input-sm w-20"
                    type="number"
                    max="9999"
                    v-model={logRetentionLimit.value}
                  />
                </div>
                <div class="setting-item">
                  <div class="setting-item-label">{t('logStyle')}</div>
                  <select
                    class="select select-sm w-28"
                    value={logDisplayStyle.value}
                    onChange={(event) =>
                      (logDisplayStyle.value = (event.target as HTMLSelectElement)
                        .value as LIST_DISPLAY_STYLE)
                    }
                  >
                    <option value={LIST_DISPLAY_STYLE.CARD}>{t('card')}</option>
                    <option value={LIST_DISPLAY_STYLE.TABLE}>{t('table')}</option>
                  </select>
                </div>
                <div class="setting-item">
                  <div class="setting-item-label shrink-0!">{t('hideLogRegex')}</div>
                  <TextInput
                    class="w-32 max-w-64 flex-1"
                    v-model={logFilterRegex.value}
                  />
                </div>
                <div class="setting-item">
                  <div class="setting-item-label flex items-center gap-2">
                    <span>{t('hideLog')}</span>
                    <div
                      onMouseenter={(e) =>
                        showTip(e, t('hideLogTip'), {
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
                    v-model={logFilterEnabled.value}
                  />
                </div>
              </div>
            </div>
          </DialogWrapper>
        </>
      )

      const buttons = (
        <div class="flex items-center gap-2">
          {settingsModal}
          <button
            class="btn btn-circle btn-sm"
            onClick={downloadAllLogs}
          >
            <ArrowDownTrayIcon class="h-4 w-4" />
          </button>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => {
              logFilterEnabled.value = !logFilterEnabled.value
              updateTip(logFilterEnabled.value ? t('showLog') : t('hideLog'))
            }}
            onMouseenter={(e) =>
              showTip(e, logFilterEnabled.value ? t('showLog') : t('hideLog'), {
                appendTo: 'parent',
              })
            }
          >
            {logFilterEnabled.value ? (
              <LinkSlashIcon class="h-4 w-4" />
            ) : (
              <LinkIcon class="h-4 w-4" />
            )}
          </button>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => (isPaused.value = !isPaused.value)}
          >
            {isPaused.value ? <PlayIcon class="h-4 w-4" /> : <PauseIcon class="h-4 w-4" />}
          </button>
          <button
            class="btn btn-circle btn-sm"
            onClick={() => (logs.value = [])}
          >
            <XMarkIcon class="h-4 w-4" />
          </button>
        </div>
      )

      const content = !isLargeCtrlsBar.value ? (
        <div class="flex flex-col gap-2 p-2">
          <div class="flex w-full justify-between gap-2">
            <div class="flex flex-1">{levelSelect}</div>
            {buttons}
          </div>
          <div class="flex w-full items-center gap-2">
            {logTypeSelect}
            {searchInput}
          </div>
        </div>
      ) : (
        <div class="flex items-center justify-between gap-2 p-2">
          <div class="flex items-center gap-2">
            {levelSelect}
            <div class="flex items-center gap-2">
              {logTypeSelect}
              {searchInput}
            </div>
          </div>
          {buttons}
        </div>
      )

      return <CtrlsBar>{content}</CtrlsBar>
    }
  },
})
