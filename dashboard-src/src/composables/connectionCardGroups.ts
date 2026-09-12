import { CONNECTIONS_TABLE_ACCESSOR_KEY } from '@/constant'
import { useStorage } from '@vueuse/core'
import { computed, ref } from 'vue'

export type ConnectionCardGroupKey =
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Type
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Process
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Host
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Rule
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Chains
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Outbound
  | CONNECTIONS_TABLE_ACCESSOR_KEY.SourceIP
  | CONNECTIONS_TABLE_ACCESSOR_KEY.SniffHost
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Destination
  | CONNECTIONS_TABLE_ACCESSOR_KEY.DestinationType
  | CONNECTIONS_TABLE_ACCESSOR_KEY.GeoIP
  | CONNECTIONS_TABLE_ACCESSOR_KEY.RemoteAddress
  | CONNECTIONS_TABLE_ACCESSOR_KEY.InboundUser
  | CONNECTIONS_TABLE_ACCESSOR_KEY.Protocol
  | CONNECTIONS_TABLE_ACCESSOR_KEY.OutboundType
  | CONNECTIONS_TABLE_ACCESSOR_KEY.FromOutbound

export const CONNECTION_CARD_GROUPABLE_KEYS: ConnectionCardGroupKey[] = [
  CONNECTIONS_TABLE_ACCESSOR_KEY.Type,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Process,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Host,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Rule,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Chains,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Outbound,
  CONNECTIONS_TABLE_ACCESSOR_KEY.SourceIP,
  CONNECTIONS_TABLE_ACCESSOR_KEY.SniffHost,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Destination,
  CONNECTIONS_TABLE_ACCESSOR_KEY.DestinationType,
  CONNECTIONS_TABLE_ACCESSOR_KEY.GeoIP,
  CONNECTIONS_TABLE_ACCESSOR_KEY.RemoteAddress,
  CONNECTIONS_TABLE_ACCESSOR_KEY.InboundUser,
  CONNECTIONS_TABLE_ACCESSOR_KEY.Protocol,
  CONNECTIONS_TABLE_ACCESSOR_KEY.OutboundType,
  CONNECTIONS_TABLE_ACCESSOR_KEY.FromOutbound,
]

export const connectionCardGroupKey = useStorage<ConnectionCardGroupKey | null>(
  'config/connection-card-group-key',
  null,
)

const groupIds = ref<string[]>([])
const expandedGroupIds = ref(new Set<string>())

export const expandedConnectionCardGroupIds = computed(() => expandedGroupIds.value)
export const hasExpandedConnectionCardGroups = computed(() =>
  groupIds.value.some((id) => expandedGroupIds.value.has(id)),
)
export const hasConnectionCardGroups = computed(() => groupIds.value.length > 0)

export const toggleConnectionCardGroup = (id: string) => {
  const next = new Set(expandedGroupIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expandedGroupIds.value = next
}

export const toggleAllConnectionCardGroups = () => {
  expandedGroupIds.value = hasExpandedConnectionCardGroups.value
    ? new Set()
    : new Set(groupIds.value)
}

export const resetConnectionCardGroups = () => {
  expandedGroupIds.value = new Set()
}

export const syncConnectionCardGroupIds = (ids: string[]) => {
  groupIds.value = ids
  const liveIds = new Set(ids)
  const retained = [...expandedGroupIds.value].filter((id) => liveIds.has(id))
  if (retained.length !== expandedGroupIds.value.size) {
    expandedGroupIds.value = new Set(retained)
  }
}
