import * as ipaddr from 'ipaddr.js'

export const isIP = (input: string) => {
  if (ipaddr.IPv4.isIPv4(input)) return 4
  if (ipaddr.IPv6.isIPv6(input)) return 6
  return 0
}

export default { isIP }
