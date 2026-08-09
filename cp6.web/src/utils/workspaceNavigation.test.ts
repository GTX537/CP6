import { describe, expect, it } from 'vitest'
import { detachedWorkspaceTarget, isDetachedWorkspacePath } from './workspaceNavigation'

describe('workspace navigation', () => {
  it.each([
    '/oa/designer',
    '/wf/form-designer',
    '/wf/flow-designer/',
  ])('opens %s in a detached tab', (path) => {
    expect(isDetachedWorkspacePath(path)).toBe(true)
  })

  it('keeps ordinary pages in the current tab', () => {
    expect(isDetachedWorkspacePath('/oa/inbox')).toBe(false)
  })

  it.each([
    ['/oa/designer', '/oa/designer/window'],
    ['/wf/form-designer/', '/wf/form-designer/window'],
    ['/wf/flow-designer', '/wf/flow-designer/window'],
  ])('maps %s to the standalone route', (path, target) => {
    expect(detachedWorkspaceTarget(path)).toBe(target)
  })

  it('does not map an ordinary route', () => {
    expect(detachedWorkspaceTarget('/oa/inbox')).toBeNull()
  })
})
