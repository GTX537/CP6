const DETACHED_WORKSPACE_PATHS = new Map([
  ['/oa/designer', '/oa/designer/window'],
  ['/wf/form-designer', '/wf/form-designer/window'],
  ['/wf/flow-designer', '/wf/flow-designer/window'],
])

function normalizePath(path: string) {
  return path.length > 1 ? path.replace(/\/+$/, '') : path
}

export function isDetachedWorkspacePath(path: string) {
  return DETACHED_WORKSPACE_PATHS.has(normalizePath(path))
}

export function detachedWorkspaceTarget(path: string) {
  return DETACHED_WORKSPACE_PATHS.get(normalizePath(path)) ?? null
}
