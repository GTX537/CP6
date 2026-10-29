import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import type { RuleView } from '@/api/wms/locationRule'
const mock=vi.hoisted(()=>({assess:vi.fn(),permission:null as any,authenticated:true}))
vi.mock('@/api/wms/locationStockAssessment',()=>({locationStockAssessmentApi:mock}))
vi.mock('@/api/wms/transferEnvironment',()=>({transferEnvironmentApi:{}}))
vi.mock('@/auth/browserAuthentication',()=>({authenticationCanRead:()=>mock.authenticated}))
vi.mock('@/stores/permission',()=>({usePermissionStore:()=>({loaded:true,has:()=>mock.permission.allowed})}))
import Panel from './LocationStockPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id=(n:number)=>`40000000-0000-4000-8000-${String(n).padStart(12,'0')}`
const scope={tenantId:id(1),actorId:id(2),locationId:id(3)}
const source:RuleView={reference:{owner:'Main.Wms.LocationRuleProposal.v1',id:id(4),version:'9007199254740993',digest:'a'.repeat(64)},locationId:id(3),etag:'"current"',redactedFields:[],selectorId:id(7),state:'DRAFT',candidate:{},originalJson:null,referenceResolution:'UNRESOLVED',approval:'NOT_SUBMITTED',admissionGrant:false}
function response(){return {code:0,data:{...scope,warehouseId:id(5),proposal:source.reference,etag:source.etag,observedAt:'2026-10-29T00:00:00Z',stockCoverage:'COMPLETE_CURRENT_ROWS',truncated:false,
  facts:[{stockId:id(6),productCode:'PRIVATE',lotNumber:'LOT',unitCode:'EA',physicalQuantity:'9999999999999.99999999',allocatedQuantity:'40.00000000',availableQuantity:'9999999999959.99999999',rowVersion:'AQ=='}],
  missingEvidence:['NON_STOCK_OBLIGATION_COVERAGE_UNPROVEN','STOCK_CAPACITY_MEASUREMENT_UNPROVEN'],sourceOwner:'Main.Wms.NativeStockObservation.v1',qualification:'CURRENT_NATIVE_STOCK_ONLY',occupancyCoverage:'UNKNOWN',capacityQuantity:null,admissionGrant:false,requiresFinalEffectRecheck:true}}}
let view:VueWrapper|undefined
async function render(){view=mount(Panel,{props:{source,scope}});await flushPromises()}
async function assess(){await view!.get('button').trigger('click');await flushPromises()}
beforeEach(()=>{vi.resetAllMocks();mock.permission=reactive({allowed:true});mock.authenticated=true;localStorage.clear();localStorage.setItem(AUTH_EPOCH,'one');mock.assess.mockResolvedValue(response())})
afterEach(()=>{view?.unmount();view=undefined})
it('binds saved candidate and preserves exact quantities without adding allocations or claiming capacity',async()=>{
  await render();await assess();expect(mock.assess).toHaveBeenCalledWith(id(3),id(4),source.etag,source.reference)
  expect(view!.text()).toContain('9999999999999.99999999');expect(view!.text()).toContain('其中已分配');expect(view!.text()).toContain('完整容量占用尚未确认');expect(view!.text()).toContain('准确单位、换算及身份关系')
})
it('explains scoped empty results without calling the location empty',async()=>{
  const r=response();r.data.facts=[];r.data.stockCoverage='VISIBLE_ROWS_ONLY';mock.assess.mockResolvedValue(r)
  await render();await assess();expect(view!.text()).toContain('不能判断整个库位是否为空');expect(view!.text()).toContain('不能据此认定容量占用为零')
})
it.each(['ARCHIVED_BALANCE_UNRESOLVED','TRUNCATED'])('shows unresolved %s coverage',async status=>{
  const r=response();r.data.stockCoverage=status;r.data.truncated=status==='TRUNCATED';mock.assess.mockResolvedValue(r)
  await render();await assess();expect(view!.find('article').exists()).toBe(true);expect(view!.text()).toContain(status==='TRUNCATED'?'前100条':'归档库存余额')
})
it.each(['tenant','candidate','grant','quantity'])('rejects inconsistent %s responses',async kind=>{
  const r=response();if(kind==='tenant')r.data.tenantId=id(9);if(kind==='candidate')r.data.proposal={...source.reference,version:'2'};if(kind==='grant')r.data.admissionGrant=true;if(kind==='quantity')(r.data.facts[0] as any).physicalQuantity=60
  mock.assess.mockResolvedValue(r);await render();await assess();expect(view!.find('article').exists()).toBe(false);expect(view!.get('[role=alert]').text()).toContain('不一致')
})
it('clears observed stock on permission and authentication changes',async()=>{
  await render();await assess();expect(view!.text()).toContain('PRIVATE');mock.permission.allowed=false;await flushPromises();expect(view!.text()).not.toContain('PRIVATE')
  mock.permission.allowed=true;await flushPromises();await assess();window.dispatchEvent(new Event(AUTH_EVENT));await flushPromises();expect(view!.text()).not.toContain('PRIVATE')
})
it('discards late data after a candidate changes or the view is disabled',async()=>{
  let finish!:(x:any)=>void;mock.assess.mockImplementation(()=>new Promise(r=>{finish=r}));await render();await assess()
  await view!.setProps({source:{...source,etag:'"new"'}});finish(response());await flushPromises();expect(view!.text()).not.toContain('PRIVATE')
  await assess();await view!.setProps({disabled:true});finish(response());await flushPromises();expect(view!.text()).not.toContain('PRIVATE')
})
