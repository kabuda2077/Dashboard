import { resetConfigs } from '@/assembly/config'
import { resetProxies } from '@/assembly/proxies'
import { resetRules } from '@/assembly/rules'
import { resetLogs } from '@/assembly/logs'
import { resetConnections } from '@/store/connections'
import { resetStatistics } from '@/store/overview'
import { smartOrderMap, smartWeightsMap } from '@/store/smart'

// Runtime data only: preferences, pause choices and persisted history survive.
export const resetBackendRuntime = () => {
  resetConnections()
  resetLogs()
  resetStatistics()
  resetConfigs()
  resetProxies()
  resetRules()
  smartOrderMap.value = {}
  smartWeightsMap.value = {}
}
