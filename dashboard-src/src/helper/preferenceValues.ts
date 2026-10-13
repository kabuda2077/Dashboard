import {
  CONNECTION_DISPLAY_STYLE,
  EMOJIS,
  FOLDER_MODE,
  FONTS,
  IP_INFO_API,
  LANG,
  LIST_DISPLAY_STYLE,
  LOG_LEVEL,
  PROXY_CARD_SIZE,
  PROXY_CHAIN_DIRECTION,
  PROXY_PREVIEW_TYPE,
  PROXY_SEARCH_MODE,
  PROXY_SORT_TYPE,
  SORT_DIRECTION,
  SORT_TYPE,
  SPEEDTEST_MODE,
  TABLE_SIZE,
  TABLE_WIDTH_MODE,
} from '@/constant'

// Raw string preferences use VueUse's string serializer (not JSON quoting).
// Keep current enum validation shared by import and storage consumers.
const enumValues: Record<string, readonly string[]> = {
  'config/language': Object.values(LANG),
  'config/font': Object.values(FONTS),
  'config/emoji': Object.values(EMOJIS),
  'config/geoip-info-api': Object.values(IP_INFO_API),
  'config/proxy-folder-mode-setting': Object.values(FOLDER_MODE),
  'config/speedtest-mode': Object.values(SPEEDTEST_MODE),
  'config/proxy-search-mode': Object.values(PROXY_SEARCH_MODE),
  'config/proxy-provider-search-mode': Object.values(PROXY_SEARCH_MODE),
  'config/proxy-sort-type': Object.values(PROXY_SORT_TYPE),
  'config/proxy-preview-type': Object.values(PROXY_PREVIEW_TYPE),
  'config/proxy-card-size': Object.values(PROXY_CARD_SIZE),
  'config/connection-display-style': Object.values(CONNECTION_DISPLAY_STYLE),
  'config/connection-sort-type': Object.values(SORT_TYPE),
  'config/connection-sort-direction': Object.values(SORT_DIRECTION),
  'config/proxy-chain-direction': Object.values(PROXY_CHAIN_DIRECTION),
  'config/connection-table-size': Object.values(TABLE_SIZE),
  'config/table-width-mode': Object.values(TABLE_WIDTH_MODE),
  'config/rule-display-style': Object.values(LIST_DISPLAY_STYLE),
  'config/log-display-style': Object.values(LIST_DISPLAY_STYLE),
  'config/log-level': Object.values(LOG_LEVEL),
  'config/connection-history-aggregation-type': [
    'sourceIP',
    'destination',
    'process',
    'outbound',
    'proxyGroup',
  ],
}

export const isValidPreferenceEnum = (key: string, value: unknown) =>
  !Object.hasOwn(enumValues, key) || (typeof value === 'string' && enumValues[key].includes(value))

const record = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
const strings = (value: unknown): value is string[] =>
  Array.isArray(value) && value.every((item) => typeof item === 'string')
const validFolderState = (value: unknown) =>
  record(value) &&
  typeof value.activeId === 'string' &&
  typeof value.seeded === 'boolean' &&
  Array.isArray(value.folders) &&
  value.folders.every(
    (folder) =>
      record(folder) &&
      typeof folder.id === 'string' &&
      typeof folder.name === 'string' &&
      typeof folder.order === 'number' &&
      Number.isFinite(folder.order) &&
      (folder.icon === undefined || typeof folder.icon === 'string') &&
      strings(folder.manualIncludes) &&
      Array.isArray(folder.rules) &&
      folder.rules.every(
        (rule) =>
          record(rule) &&
          typeof rule.type === 'string' &&
          (rule.type === 'auto'
            ? rule.value === 'nodeOnly' || rule.value === 'hasGroup'
            : rule.type === 'regex' || rule.type === 'excludeRegex'
              ? typeof rule.pattern === 'string'
              : true),
      ),
  )

const validHistorySorting = (value: unknown) =>
  Array.isArray(value) &&
  value.every(
    (item) =>
      record(item) &&
      typeof item.id === 'string' &&
      ['key', 'download', 'upload', 'total', 'count'].includes(item.id) &&
      typeof item.desc === 'boolean',
  ) &&
  new Set(value.map((item) => item.id)).size === value.length

export const isValidPreferenceValue = (key: string, value: unknown) =>
  key === 'config/proxy-folders'
    ? validFolderState(value)
    : key === 'config/connection-history-sorting'
      ? validHistorySorting(value)
      : isValidPreferenceEnum(key, value)

export const validatePreferenceValues = (values: Record<string, unknown>) => {
  for (const [key, value] of Object.entries(values)) {
    let decoded = value
    if (
      (key === 'config/proxy-folders' || key === 'config/connection-history-sorting') &&
      typeof value === 'string'
    ) {
      try {
        decoded = JSON.parse(value)
      } catch {
        throw new Error(`Invalid preference value: ${key}`)
      }
    }
    if (!isValidPreferenceValue(key, decoded)) throw new Error(`Invalid preference value: ${key}`)
  }
}
