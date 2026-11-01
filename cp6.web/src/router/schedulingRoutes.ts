import type { RouteRecordRaw } from 'vue-router'
export const schedulingRoutes: RouteRecordRaw[] = [
  ...(['operations', 'changes', 'previews', 'work'] as const).map(kind => ({
    path: `mes/planning-board/${kind}/:id`, name: `mes/scheduling-${kind}`,
    component: () => import('@/views/mes/SchedulingDetailView.vue'),
    props: (route: any) => ({ kind, id: route.params.id, workKind: route.query.kind }),
    meta: { permission: `mes-planning-board:${kind === 'work' ? 'work.read' : kind === 'previews' ? 'preview.read' : 'query'}` }
  })),
  { path: 'mes/resource-queues/:id', name: 'mes/resource-queue', component: () => import('@/views/mes/SchedulingDetailView.vue'),
    props: route => ({ kind: 'queue', id: route.params.id, siteId: route.query.siteId }), meta: { permission: 'mes-planning-board:queue.read' } }
]
