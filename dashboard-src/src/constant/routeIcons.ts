import { ROUTE_NAME } from '@/constant'
import {
  ArrowsRightLeftIcon,
  CpuChipIcon,
  CubeTransparentIcon,
  DocumentTextIcon,
  GlobeAltIcon,
  Squares2X2Icon,
  SwatchIcon,
} from '@heroicons/vue/24/outline'

export const ROUTE_ICON_MAP = {
  [ROUTE_NAME.core]: CpuChipIcon,
  [ROUTE_NAME.overview]: Squares2X2Icon,
  [ROUTE_NAME.proxies]: GlobeAltIcon,
  [ROUTE_NAME.connections]: ArrowsRightLeftIcon,
  [ROUTE_NAME.rules]: SwatchIcon,
  [ROUTE_NAME.logs]: DocumentTextIcon,
  [ROUTE_NAME.setup]: CubeTransparentIcon,
}
