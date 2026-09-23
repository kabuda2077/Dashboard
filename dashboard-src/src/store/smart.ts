import { fetchSmartGroupWeightsAPI, fetchSmartWeightsAPI } from '@/assembly/proxies'
import { captureBackendSession } from '@/helper/backendSession'
import type { NodeRank } from '@/types'
import { ref } from 'vue'

export const smartWeightsMap = ref<Record<string, Record<string, string>>>({})
export const smartOrderMap = ref<Record<string, Record<string, number>>>({})

let smartWeightsRequest = 0

const restructWeights = (weights: NodeRank[]) => {
  const smartWeights: Record<string, string> = {}
  const smartOrder: Record<string, number> = {}

  weights.forEach((weight, index) => {
    smartWeights[weight.Name] = weight.Rank
    smartOrder[weight.Name] = index
  })

  return { smartWeights, smartOrder }
}

const applyWeights = (
  weightsByGroup: Record<string, NodeRank[]>,
  session: ReturnType<typeof captureBackendSession>,
  request: number,
) => {
  if (!session.isCurrent() || request !== smartWeightsRequest) return

  const weightsMap: Record<string, Record<string, string>> = {}
  const orderMap: Record<string, Record<string, number>> = {}

  for (const [group, weights] of Object.entries(weightsByGroup)) {
    if (!weights?.length) continue

    const { smartWeights, smartOrder } = restructWeights(weights)
    weightsMap[group] = smartWeights
    orderMap[group] = smartOrder
  }

  smartWeightsMap.value = weightsMap
  smartOrderMap.value = orderMap
}

const fetchLegacySmartWeights = async (smartGroups: string[]) => {
  const entries = await Promise.all(
    smartGroups.map(async (group) => {
      const { data } = await fetchSmartGroupWeightsAPI(group)
      return [group, data.weights] as const
    }),
  )

  return Object.fromEntries(entries)
}

export const initSmartWeights = async (smartGroups: string[]) => {
  const session = captureBackendSession()
  const request = ++smartWeightsRequest
  const { status, data } = await fetchSmartWeightsAPI()

  if (!session.isCurrent() || request !== smartWeightsRequest) return

  if (status === 404) {
    // Compatibility with Smart cores that only expose the deprecated per-group endpoint.
    const legacyWeights = await fetchLegacySmartWeights(smartGroups)
    applyWeights(legacyWeights, session, request)
    return
  }

  if (status !== 200) {
    throw new Error(`Unexpected Smart weights response: HTTP ${status}`)
  }

  applyWeights(data.weights, session, request)
}
