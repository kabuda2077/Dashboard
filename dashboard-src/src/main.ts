// Keep this entry free of imports that initialize Vue stores or storage defaults.
import { startDashboard } from './helper/dashboardStartup'

void startDashboard(() => import('./appEntry'))
