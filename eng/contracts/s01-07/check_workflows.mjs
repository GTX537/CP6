// 兼容原有调用路径，显式传入报告路径时才写文件。
import { runWorkflowAudit } from '../../../scripts/check-workflows.mjs'

runWorkflowAudit()
