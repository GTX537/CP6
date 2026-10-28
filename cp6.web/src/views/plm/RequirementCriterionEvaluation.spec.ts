import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { RequirementCriterion } from '@/api/plm/engineeringRequirements'
const api = vi.hoisted(() => ({ list: vi.fn(), read: vi.fn() }))
vi.mock('@/api/erp/uomProfiles', () => ({ uomApi: api }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
import Unit from './RequirementCriterionEvaluation.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = { owner: 'Main.Erp.UomProfile.v1', id: '00000000-0000-4000-8000-000000000001', version: '3', digest: 'a'.repeat(64) }, exact = '999999999999999999999999.000000000000000000001'
let view: VueWrapper | undefined, criterion: RequirementCriterion
const button = (label: string) => view!.findAll('button').find(x => x.text() === label)!
async function choose() { await button('查找评价单位').trigger('click'); await flushPromises(); await view!.get('select').setValue(`${reference.id}:3:${reference.digest}`); await button('采用评价单位').trigger('click'); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); criterion = { id: 'C', kind: 'QUANTITATIVE', object: 'Body', conditions: 'Ambient', methodRequirement: 'Load', lower: exact, lowerInclusive: true, upper: null, upperInclusive: null, proposedUnit: 'source unit', unitBasis: 'Source clause', precision: 'Preserve original', qualitativeBasis: null }; const value = { reference, isCurrent: true, profile: { code: 'N', name: 'newton', dimensionKey: 'FORCE', enabled: true } }; api.list.mockResolvedValue({ code: 0, data: [value] }); api.read.mockResolvedValue({ code: 0, data: value }) })
afterEach(() => view?.unmount())
it('adopts the exact native unit without converting precision and keeps omitted upper bound unresolved until explicit choice', async () => {
  view = mount(Unit, { props: { criterion, index: 1 } }); await choose(); expect(criterion.lower).toBe(exact); expect(criterion.precision).toBe('Preserve original'); expect(criterion.evaluation).toMatchObject({ unit: reference, lowerUnbounded: false, upperUnbounded: false }); expect(criterion.proposedUnit).toBe('N'); expect(view.get('[aria-label="明确无下界 1"]').attributes('disabled')).toBeDefined(); await view.get('[aria-label="明确无上界 1"]').setValue(true); expect(criterion.evaluation?.upperUnbounded).toBe(true)
})
it('rejects stale native unit and clears choices on identity change', async () => {
  api.read.mockResolvedValueOnce({ code: 0, data: { reference, isCurrent: false, profile: { enabled: true } } }); view = mount(Unit, { props: { criterion, index: 1 } }); await choose(); expect(criterion.evaluation).toBeUndefined(); expect(criterion.proposedUnit).toBe('source unit'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view.findAll('option')).toHaveLength(1)
})
