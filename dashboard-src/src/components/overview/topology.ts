export interface TopologyConnection {
  source: string
  rule: string
  chains: string[]
}

export interface TopologyLabels {
  sourceIPAddress: string
  ruleMatch: string
  proxyChainEntry: string
  proxyChainExit: string
  unknown: string
}

export interface TopologyNode {
  id: number
  name: string
  nodeType: string
  layer: number
  itemStyle: {
    color: string
  }
}

export interface TopologyLink {
  source: number
  target: number
  value: number
  originalValue: number
}

export interface TopologyData {
  nodes: TopologyNode[]
  links: TopologyLink[]
}

export const TOPOLOGY_LAYER_COLORS = ['#6a6fc5', '#a8d4a0', '#fddb8a', '#f2a0a0']

export const buildTopologyData = (
  connections: readonly TopologyConnection[],
  labels: TopologyLabels,
): TopologyData => {
  const nodeIds = new Map<string, number>()
  const nodeNames = new Map<string, string>()
  const nodeLayers = new Map<string, number>()
  const nodeTypes = new Map<string, string>()
  const linkCounts = new Map<string, number>()
  let nextNodeId = 0

  const addNode = (name: string, layer: number, type: string) => {
    const key = `${layer}:${name}`

    if (!nodeIds.has(key)) {
      nodeIds.set(key, nextNodeId++)
      nodeNames.set(key, name)
      nodeLayers.set(key, layer)
      nodeTypes.set(key, type)
    }

    return nodeIds.get(key)!
  }

  const addLink = (source: number, target: number) => {
    const key = `${source}:${target}`
    linkCounts.set(key, (linkCounts.get(key) ?? 0) + 1)
  }

  for (const connection of connections) {
    if (connection.chains.length === 0) continue

    const chainExit = connection.chains[0]
    const chainEntry = connection.chains.at(-1)!
    const sourceNode = addNode(connection.source, 0, labels.sourceIPAddress)
    const ruleNode = addNode(connection.rule, 1, labels.ruleMatch)

    addLink(sourceNode, ruleNode)

    if (chainEntry === chainExit) {
      addLink(ruleNode, addNode(chainExit, 3, labels.proxyChainExit))
      continue
    }

    const entryNode = addNode(chainEntry, 2, labels.proxyChainEntry)
    const exitNode = addNode(chainExit, 3, labels.proxyChainExit)
    addLink(ruleNode, entryNode)
    addLink(entryNode, exitNode)
  }

  const initialNodes = Array.from(nodeIds, ([key, id]): TopologyNode => {
    const layer = nodeLayers.get(key) ?? 0

    return {
      id,
      name: nodeNames.get(key) ?? '',
      nodeType: nodeTypes.get(key) ?? labels.unknown,
      layer,
      itemStyle: {
        color: TOPOLOGY_LAYER_COLORS[layer],
      },
    }
  })

  const nodesByLayer = new Map<number, TopologyNode[]>()
  for (const node of initialNodes) {
    const nodes = nodesByLayer.get(node.layer) ?? []
    nodes.push(node)
    nodesByLayer.set(node.layer, nodes)
  }

  const idMapping = new Map<number, number>()
  const nodes: TopologyNode[] = []

  for (const layer of Array.from(nodesByLayer.keys()).sort((a, b) => a - b)) {
    const layerNodes = nodesByLayer.get(layer)!
    layerNodes.sort((a, b) => a.name.localeCompare(b.name))

    for (const node of layerNodes) {
      const id = nodes.length
      idMapping.set(node.id, id)
      nodes.push({ ...node, id })
    }
  }

  const links = Array.from(linkCounts, ([key, originalValue]) => {
    const [oldSource, oldTarget] = key.split(':').map(Number)
    const source = idMapping.get(oldSource)
    const target = idMapping.get(oldTarget)

    if (source === undefined || target === undefined || source === target) return null

    return {
      source,
      target,
      value: Math.log10(originalValue + 1) * 10,
      originalValue,
    }
  }).filter((link): link is TopologyLink => link !== null)

  return { nodes, links }
}
