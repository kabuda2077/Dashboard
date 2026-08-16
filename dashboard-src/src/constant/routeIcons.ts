import { ROUTE_NAME } from '@/constant'
import {
  ArrowsRightLeftIcon,
  CpuChipIcon,
  CubeTransparentIcon,
  DocumentTextIcon,
  GlobeAltIcon,
  SwatchIcon,
} from '@heroicons/vue/24/outline'

export const ROUTE_ICON_MAP = {
  [ROUTE_NAME.core]: CpuChipIcon,
  [ROUTE_NAME.overview]: CubeTransparentIcon,
  [ROUTE_NAME.proxies]: GlobeAltIcon,
  [ROUTE_NAME.connections]: ArrowsRightLeftIcon,
  [ROUTE_NAME.rules]: SwatchIcon,
  [ROUTE_NAME.logs]: DocumentTextIcon,
  [ROUTE_NAME.setup]: CubeTransparentIcon,
}
