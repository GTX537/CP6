import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import Panel from './DocumentAvailabilityPanel.vue'
import type { DocumentSnapshot } from '@/api/plm/controlledDocument'
const document = { reference: { owner: 'Main.Plm.ControlledDocument.v1', id: 'document', version: '2', digest: 'exact digest' }, body: { name: '准确旧修订' }, availability: { stateVersion: '4', etag: 'current token', state: 'AVAILABLE', action: 'RELEASE', reason: 'Current scope released' } } as DocumentSnapshot
const button = (view: ReturnType<typeof mount>, text: string) => view.findAll('button').find(x => x.text() === text)!
it('requires explicit exact-revision confirmation and cancellation has no effect', async () => {
  const view = mount(Panel, { props: { document, disabled: false, canManage: true } })
  await view.get('select').setValue('WITHDRAW'); await view.get('textarea').setValue('Explicit revision withdrawal'); await view.get('form').trigger('submit')
  expect(view.get('[role=alert]').text()).toContain('准确旧修订 · 修订 2'); expect(view.emitted('change')).toBeUndefined()
  await button(view, '取消处理').trigger('click'); expect(view.emitted('change')).toBeUndefined()
  await view.get('form').trigger('submit'); await button(view, '确认处理此修订').trigger('click')
  expect(view.emitted('change')![0]![0]).toEqual({ document: document.reference, action: 'WITHDRAW', expectedStateVersion: '4', expectedEtag: 'current token', reason: 'Explicit revision withdrawal' })
  view.unmount()
})
it('invalidates prepared intent when current state or permission changes', async () => {
  const view = mount(Panel, { props: { document, disabled: false, canManage: true } })
  await view.get('select').setValue('HOLD'); await view.get('textarea').setValue('Review needed'); await view.get('form').trigger('submit')
  await view.setProps({ document: { ...document, availability: { ...document.availability, etag: 'next token', state: 'HELD' } } })
  expect(view.find('[role=alert]').exists()).toBe(false); expect(view.find('option[value=HOLD]').exists()).toBe(false)
  await view.get('select').setValue('RELEASE'); await view.get('textarea').setValue('Current release'); await view.get('form').trigger('submit')
  await view.setProps({ canManage: false }); expect(view.find('form').exists()).toBe(false); expect(view.find('[role=alert]').exists()).toBe(false)
  view.unmount()
})
it('shows withdrawn history without offering resurrection or allowing a pending operation', async () => {
  const view = mount(Panel, { props: { document: { ...document, availability: { ...document.availability, state: 'WITHDRAWN' } }, disabled: false, canManage: true } })
  expect(view.text()).toContain('已撤回'); expect(view.find('form').exists()).toBe(false)
  await button(view, '读取可用性记录').trigger('click'); expect(view.emitted('history')).toHaveLength(1)
  await view.setProps({ document, disabled: true }); expect(view.get('fieldset').attributes('disabled')).toBeDefined()
  await view.get('select').setValue('HOLD'); await view.get('textarea').setValue('No second command'); await view.get('form').trigger('submit')
  expect(view.find('[role=alert]').exists()).toBe(false); expect(view.emitted('change')).toBeUndefined(); view.unmount()
})
