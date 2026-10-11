"""Authored W1 design asset generator. Does not run archived code or access a database."""
from pathlib import Path
import json, hashlib, re

HERE = Path(__file__).resolve().parent
BOOK = HERE.parents[2]
ORIGINALS = Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
VERSION = 'CP6-COMMERCIAL-PHYSICAL/1.0.0'

def write(name, value):
    (HERE/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def obj(properties, required=None):
    return {'type':'object','additionalProperties':False,'properties':properties,'required':list(properties) if required is None else required}
def ref(name): return {'$ref':'#/components/schemas/'+name}
def arr(item, maximum=200, minimum=0): return {'type':'array','items':item,'minItems':minimum,'maxItems':maximum}
def string(n=256, minimum=1): return {'type':'string','minLength':minimum,'maxLength':n}
def enum(*values): return {'type':'string','enum':list(values)}
def nullable(schema): return {'anyOf':[schema,{'type':'null'}]}
def const(value): return {'const':value,'type':'string'}

S={}
S['Key']=string(256)
S['Revision']={'type':'string','pattern':'^[1-9][0-9]{0,18}$','description':'New local revision: <=9223372036854775807. External revisions remain opaque Key; never compare across streams.'}
S['Digest']={'type':'string','pattern':'^[0-9a-f]{64}$'}
S['Utc']={'type':'string','format':'date-time','pattern':'Z$'}
S['Decimal']={'type':'string','pattern':'^(0|[1-9][0-9]{0,17})(\\.[0-9]{1,6})?$','description':'Exact nonnegative decimal(24,6); canonical form strips fractional trailing zeroes; no exponent. Business UOM scale may be narrower.'}
S['SignedDecimal']={'type':'string','pattern':'^-?(0|[1-9][0-9]{0,17})(\\.[0-9]{1,6})?$'}
S['Error']=obj({'code':string(80),'messageKey':string(100),'fieldPaths':arr(string(256)),'correlationId':ref('Key'),'recovery':enum('FIX_INPUT','READ_CURRENT','QUERY_ORIGINAL','CONTACT_OWNER','NONE'),'originalOutcomeUnchanged':{'const':True}})
S['OwnerRef']=obj({'owner':ref('Key'),'contractId':ref('Key'),'contractVersion':ref('Key'),'evidenceId':ref('Key'),'evidenceVersion':ref('Key'),'contentDigest':ref('Digest')})
S['FactWatermark']=obj({'owner':ref('Key'),'scopeDigest':ref('Digest'),'version':nullable(ref('Key')),'complete':{'type':'boolean'},'observedAt':ref('Utc'),'unknownReason':nullable(string(200))})
S['Condition']=obj({'conditionId':ref('Key'),'ownerContract':ref('Key'),'policy':ref('OwnerRef'),'requiredness':enum('REQUIRED','JUSTIFIED_NOT_APPLICABLE','UNDETERMINED'),'evidenceState':enum('SATISFIED','NOT_SATISFIED','UNKNOWN','STALE'),'evidence':nullable(ref('OwnerRef')),'requiredBefore':enum('DRAFT','CONFIRM','RELEASE','CHANGE','CLOSE'),'blockedActions':arr(ref('Key'))})
S['ReceiptLocator']=obj({'operation':ref('Key'),'commandKey':ref('Key')})
S['ReceiptQuery']={'oneOf':[obj({'kind':const('COMMAND'),'locator':ref('ReceiptLocator')}),obj({'kind':const('MAIN_REQUEST'),'authority':ref('Key'),'accountId':ref('Key'),'requestId':ref('Key'),'requestVersion':ref('Key')})]}
S['CommandReceipt']=obj({'locator':ref('ReceiptLocator'),'receiptId':ref('Key'),'outcome':enum('COMMITTED','NO_CHANGE','REJECTED_FINAL','IN_FLIGHT','NOT_OBSERVED','EXPIRED_UNPROVEN'),'scope':enum('ENTITY_APPLY','LOCAL_REQUEST','PROJECTION'),'targetId':nullable(ref('Key')),'targetVersion':nullable(ref('Key')),'completedAt':nullable(ref('Utc')),'canOpen':{'type':'boolean'},'replayed':{'type':'boolean'},'noEffectProven':{'type':'boolean'},'finalityEvidence':nullable(ref('OwnerRef'))})
S['CommandReceipt']['properties']['receiptId']=nullable(ref('Key'))
S['CommandReceipt']['allOf']=[{'if':{'properties':{'outcome':{'enum':['COMMITTED','NO_CHANGE','REJECTED_FINAL']}}},'then':{'properties':{'receiptId':ref('Key'),'completedAt':ref('Utc')}}},{'if':{'properties':{'outcome':{'const':'NOT_OBSERVED'}}},'then':{'properties':{'receiptId':{'type':'null'},'targetId':{'type':'null'},'targetVersion':{'type':'null'},'completedAt':{'type':'null'},'noEffectProven':{'const':False}}}},{'if':{'properties':{'noEffectProven':{'const':True}}},'then':{'properties':{'finalityEvidence':ref('OwnerRef')}}}]
S['ReceiptRead']=obj({'knowledge':enum('FOUND','IN_FLIGHT','NOT_OBSERVED','EXPIRED_UNPROVEN'),'scope':enum('ENTITY_APPLY','LOCAL_REQUEST','REMOTE_BUSINESS','PROJECTION'),'command':nullable(ref('CommandReceipt')),'originalOwnerResult':nullable(ref('OwnerRef')),'historicalOutcome':ref('Key'),'currentApplicability':enum('CURRENT','INACTIVE','REVIEW_REQUIRED','UNKNOWN','RESTRICTED'),'projectionCoverage':enum('APPLIED','PENDING','PARTIAL','UNKNOWN'),'canOpen':{'type':'boolean'},'noEffectProven':{'type':'boolean'},'finalityEvidence':nullable(ref('OwnerRef')),'asOf':ref('Utc')})
S['PageQuery']=obj({'text':nullable(string(200)),'cursor':nullable(string(2048)),'limit':{'type':'integer','minimum':1,'maximum':100},'sort':enum('ID_ASC','UPDATED_DESC'),'lifecycle':nullable(ref('Key'))})
S['DuplicateReview']=obj({'reviewToken':string(2048),'reason':string(1000)})
profile={'displayName':string(200),'shortName':nullable(string(80)),'aliases':arr(string(80),10),'accountType':enum('PROSPECT','CUSTOMER','PARTNER','OTHER'),'industryCode':nullable(string(32)),'website':nullable(string(300)),'countryCode':nullable(string(2,2)),'province':nullable(string(64)),'city':nullable(string(64)),'address':nullable(string(300)),'companyPhone':nullable(string(32)),'companyEmail':nullable(string(254)),'notes':nullable(string(2000))}
S['NativeAccountCreateCp22']=obj({**profile,'departmentId':ref('Key'),'ownerUserId':nullable(ref('Key')),'duplicateReview':nullable(ref('DuplicateReview'))},['displayName','accountType','departmentId'])
S['DictionaryValue']=obj({'code':string(64),'dictionaryVersion':ref('Key')})
S['SourceHint']=obj({'owner':ref('Key'),'id':ref('Key'),'version':ref('Key')})
profile.update({'domains':arr(string(300),20),'accountType':nullable(ref('DictionaryValue')),'industryCode':nullable(ref('DictionaryValue')),'region':nullable(ref('DictionaryValue'))})
S['AccountCreate']=obj({**profile,'departmentId':ref('Key'),'ownerUserId':nullable(ref('Key')),'duplicateReview':nullable(ref('DuplicateReview')),'manualSourceReference':nullable(ref('SourceHint'))},['displayName','departmentId'])
S['AccountPatch']=obj({**profile,'departmentId':ref('Key'),'ownerUserId':nullable(ref('Key')),'duplicateReview':nullable(ref('DuplicateReview'))},[])
S['AccountPatch']['minProperties']=1
S['AccountPatch']['description']='A04 successor explicitly permits assignment fields under assignment capability. Presence tracked; duplicateReview alone rejected. CP22 native UpdateAccountRequest remains separate. v2 nullable versioned dictionaries and multi-domain input preserve R01 semantics.'
S['Reason']=obj({'reason':string(1000)})
S['CandidateQuery']=obj({'displayName':string(200),'website':nullable(string(300)),'normalizationVersion':const('ACC-NORM/1')})
S['Candidate']=obj({'accountId':ref('Key'),'version':ref('Key'),'displayName':string(200),'reasons':arr(enum('NAME','ALIAS','DOMAIN'))})
S['CandidateRead']=obj({'items':arr(ref('Candidate'),100),'asOf':ref('Utc'),'reviewToken':nullable(string(2048)),'globalUniquenessProven':{'const':False}})
S['EligibilityStamp']=obj({'accountLifecycleRevision':ref('Revision'),'identityBusinessRevision':ref('Revision'),'privacyAuthorizationWatermark':ref('Key'),'eligibilityPolicyVersion':ref('Key')})
S['AccountRead']=obj({'accountId':ref('Key'),'etag':string(512),'lifecycle':enum('ACTIVE','INACTIVE'),'privacy':enum('RETAINED','ANONYMIZED'),'profile':obj(profile,[]),'departmentId':nullable(ref('Key')),'ownerUserId':nullable(ref('Key')),'identityBusinessRevision':ref('Revision'),'eligibility':ref('EligibilityStamp'),'redactedFields':arr(ref('Key')),'asOf':ref('Utc')})
S['AccountPage']=obj({'items':arr(ref('AccountRead'),100),'nextCursor':nullable(string(2048)),'coverage':enum('AUTHORIZED_SCOPE'),'asOf':ref('Utc')})
S['SourceQuery']=obj({'sourceKind':ref('Key'),'sourceId':ref('Key'),'proofId':nullable(ref('Key')),'view':enum('AS_RECORDED','CURRENT')})
S['SourceProofNotice']=obj({'schemaVersion':const('ACC-SOURCE-PROOF/1'),'eventId':ref('Key'),'accountId':ref('Key'),'sourceProof':ref('OwnerRef'),'predecessorDigest':nullable(ref('Digest'))})
S['SourceRead']=obj({'sourceProof':nullable(ref('OwnerRef')),'historicalState':enum('SOURCE_HINT','CONVERSION_CONFIRMED','SUPERSEDED','INVALIDATED','UNCONFIRMED'),'currentKnowledge':enum('PROVEN','UNPROVEN'),'asOf':ref('Utc')})
S['NavigationRead']=obj({'allowed':{'type':'boolean'},'registeredRouteId':nullable(ref('Key')),'opaqueNavigationHandle':nullable(string(2048)),'expiresAt':nullable(ref('Utc'))})
S['LegacyResultNotice']=obj({'schemaVersion':const('ACC-C03-RESULT-REFERENCE/1'),'eventId':ref('Key'),'accountId':ref('Key'),'requestId':ref('Key'),'requestVersion':ref('Key'),'originalC03':ref('OwnerRef')})
S['ProjectionReceipt']=obj({'eventId':ref('Key'),'receiptId':ref('Key'),'status':enum('APPLIED','DUPLICATE','HISTORY_ONLY','WAITING_PREDECESSOR','WAIT_OWNER_RESOLUTION','CONFLICT'),'slotRevision':nullable(ref('Revision')),'currentKnowledge':enum('PROVEN','UNKNOWN','RESTRICTED'),'asOf':ref('Utc')})
S['MappingScope']=obj({'mappingAuthority':ref('Key'),'scopeIdentity':ref('Key'),'scopeEpoch':ref('Key'),'organizationId':ref('Key'),'legalEntityKey':ref('Key'),'accountId':ref('Key'),'mappingPurpose':const('ACCOUNT_BUSINESS_PARTNER')})
S['MappingCandidate']=obj({'ownerMappingId':ref('Key'),'ownerMappingRevision':ref('Key'),'businessPartnerKey':string(128),'boundAccountBusinessVersion':ref('Revision'),'candidateDigest':ref('Digest')})
S['DecisionPointer']=obj({'decisionId':ref('Key'),'position':ref('Revision'),'digest':ref('Digest')})
S['MappingDecision']=obj({'schemaVersion':const('ACC-MAPPING-DECISION/1'),'scope':ref('MappingScope'),'decision':ref('DecisionPointer'),'basis':enum('SUCCESSOR','COMPLETE_SNAPSHOT','CONFLICT_RESOLUTION'),'predecessor':nullable(ref('DecisionPointer')),'candidates':arr(ref('MappingCandidate'),200),'selectedCandidateDigest':nullable(ref('Digest')),'decisionKind':enum('SELECT','UNLINK'),'coveredCandidateDigests':arr(ref('Digest'),200),'coveredConflictDigests':arr(ref('Digest'),200),'completeAtWatermark':ref('Key'),'coversEntireScope':{'type':'boolean'},'effectiveFrom':ref('Utc'),'effectiveUntil':nullable(ref('Utc')),'identityImpactPolicy':ref('OwnerRef'),'authorityProof':ref('OwnerRef')})
S['MappingDecision']['allOf']=[{'if':{'properties':{'decisionKind':{'const':'SELECT'}}},'then':{'properties':{'selectedCandidateDigest':ref('Digest'),'candidates':{'minItems':1}}},'else':{'properties':{'selectedCandidateDigest':{'type':'null'}}}},{'if':{'properties':{'basis':{'const':'SUCCESSOR'}}},'then':{'properties':{'predecessor':ref('DecisionPointer')}},'else':{'properties':{'coversEntireScope':{'const':True}}}}]
S['MappingNotice']=obj({'eventId':ref('Key'),'decision':ref('MappingDecision')})
S['MappingQuery']=obj({'scope':ref('MappingScope'),'knownDecision':nullable(ref('DecisionPointer'))})
S['MappingRead']=obj({'decision':nullable(ref('MappingDecision')),'knowledge':enum('PROVEN','UNKNOWN','CONFLICT'),'asOf':ref('Utc')})
S['MappingAuthorityProof']=obj({'schemaVersion':const('ACC-MAPPING-AUTHORITY-PROOF/1'),'issuer':ref('Key'),'scope':ref('MappingScope'),'decisionDigest':ref('Digest'),'completeAtWatermark':ref('Key'),'signedAt':ref('Utc'),'purpose':const('CRM_CURRENT_MAPPING_DECISION'),'keyId':ref('Key'),'algorithm':const('Ed25519'),'signatureBase64':string(256)})
S['IdentityImpactPolicy']=obj({'schemaVersion':const('ACC-IDENTITY-IMPACT/1'),'version':const('1'),'fields':{'const':['displayName','aliases','domains','accountType','countryCode','province','city','address']},'normalizationVersion':const('ACC-NORM/1')})
S['EligibilityRead']=obj({'scope':ref('MappingScope'),'decision':nullable(ref('DecisionPointer')),'currentMapping':nullable(ref('MappingCandidate')),'eligibility':ref('EligibilityStamp'),'eligibleForNewUse':{'type':'boolean'},'reasonCodes':arr(ref('Key')),'asOf':ref('Utc'),'isExecutionPermit':{'const':False}})

# ORD/DEM v1 requests store exact owner references, never client-supplied approval booleans.
S['OrderRef']=obj({'orderId':ref('Key'),'orderVersion':ref('Key'),'orderLineId':ref('Key'),'scheduleId':ref('Key')})
S['OrderReadScope']=obj({'orderId':ref('Key'),'orderVersion':ref('Key'),'orderLineId':nullable(ref('Key')),'scheduleId':nullable(ref('Key'))})
S['Quantity']=obj({'value':ref('Decimal'),'uom':ref('OwnerRef')})
S['BusinessDate']=obj({'kind':enum('DATE','INSTANT'),'date':nullable({'type':'string','format':'date'}),'instantUtc':nullable(ref('Utc')),'node':ref('Key'),'timeZone':ref('Key'),'calendar':ref('OwnerRef')})
S['BusinessDate']['allOf']=[{'if':{'properties':{'kind':{'const':'DATE'}}},'then':{'properties':{'date':{'type':'string','format':'date'},'instantUtc':{'type':'null'}}},'else':{'properties':{'date':{'type':'null'},'instantUtc':ref('Utc')}}}]
S['TechnicalScope']=obj({'item':ref('OwnerRef'),'revision':nullable(ref('OwnerRef')),'baseline':nullable(ref('OwnerRef')),'configuration':nullable(ref('OwnerRef')),'applicability':arr(ref('Condition'))})
S['SupplyTarget']=obj({'transactionQuantity':ref('Quantity'),'standardQuantity':ref('Quantity'),'conversion':nullable(ref('OwnerRef')),'site':ref('Key'),'requiredDate':ref('BusinessDate'),'technical':ref('TechnicalScope'),'supplyMode':enum('STOCK','NEW_MANUFACTURE','PURCHASE','REWORK'),'purposeEvidence':arr(ref('OwnerRef'))})
S['ControlScope']=obj({'order':ref('OrderRef'),'releaseLineageId':ref('Key'),'actionSet':arr(enum('PLAN','CONVERT','RELEASE','MANUFACTURE','SHIP'),5,1)})
S['SourceControl']=obj({'controlId':ref('Key'),'controlVersion':ref('Revision'),'owner':ref('Key'),'reasonId':ref('Key'),'scope':ref('ControlScope'),'effectiveAt':ref('Utc'),'disposition':enum('BLOCK','RELEASE_OWN_REASON','TERMINATE'),'authority':ref('OwnerRef'),'predecessor':nullable(ref('OwnerRef'))})
S['SourceOperation']=obj({'schemaVersion':const('ORD-DEM-SOURCE/1'),'operationKey':ref('Key'),'kind':enum('INITIAL_RELEASE','REVISE_TARGET','REVISE_NONQUANTITY','CANCEL_EXISTING','CONTROL_SOURCE','PREPARE_SUCCESSOR'),'order':ref('OrderRef'),'salesReleaseId':ref('Key'),'salesReleaseLineId':ref('Key'),'releaseLineageId':ref('Key'),'sourceVersion':ref('Revision'),'expectedAcceptedSourceVersion':nullable(ref('Revision')),'previousIssuedSourceVersion':nullable(ref('Revision')),'interveningFinalResults':arr(ref('OwnerRef')),'target':nullable(ref('SupplyTarget')),'controls':arr(ref('SourceControl')),'approvalBindings':arr(ref('OwnerRef')),'cancelAllocation':nullable(ref('OwnerRef')),'activationManifest':nullable(ref('OwnerRef')),'sourceDigest':ref('Digest')})
S['SourceOperation']['properties']['salesReleaseId']=nullable(ref('Key'))
S['SourceOperation']['properties']['salesReleaseLineId']=nullable(ref('Key'))
S['SourceOperation']['allOf']=[{'if':{'properties':{'kind':{'const':'CONTROL_SOURCE'}}},'then':{'properties':{'target':{'type':'null'},'controls':{'minItems':1}}},'else':{'properties':{'target':ref('SupplyTarget'),'salesReleaseId':ref('Key'),'salesReleaseLineId':ref('Key')}}},{'if':{'properties':{'kind':{'const':'CANCEL_EXISTING'}}},'then':{'properties':{'cancelAllocation':ref('OwnerRef'),'expectedAcceptedSourceVersion':ref('Revision')}}},{'if':{'properties':{'kind':{'const':'PREPARE_SUCCESSOR'}}},'then':{'properties':{'activationManifest':ref('OwnerRef')}}}]
S['DemandCurrent']=obj({'releaseLineageId':ref('Key'),'acceptedSourceVersion':nullable(ref('Revision')),'demandId':nullable(ref('Key')),'demandVersion':nullable(ref('Revision')),'target':nullable(ref('SupplyTarget')),'controls':arr(ref('SourceControl')),'effectivity':enum('NONE','EFFECTIVE','PREPARED','TERMINATED','REPLACED','UNKNOWN'),'successors':arr(ref('OwnerRef')),'watermark':ref('FactWatermark')})
S['SourceResult']=obj({'resultId':ref('Key'),'resultVersion':ref('Revision'),'operationKey':ref('Key'),'sourceDigest':ref('Digest'),'sourceVersion':ref('Revision'),'outcome':enum('BUSINESS_ACCEPTED','BUSINESS_REJECTED','SOURCE_CONTROL_APPLIED','PREPARED_NOT_EFFECTIVE'),'demandId':nullable(ref('Key')),'demandVersion':nullable(ref('Revision')),'acceptedTarget':nullable(ref('SupplyTarget')),'controlRefs':arr(ref('OwnerRef')),'reasonCodes':arr(ref('Key')),'committedAt':ref('Utc')})
S['SourceResultQuery']=obj({'operationKey':ref('Key'),'releaseLineageId':ref('Key'),'sourceVersion':ref('Revision'),'sourceDigest':ref('Digest')})
S['SourceResultRead']=obj({'knowledge':enum('FOUND','IN_FLIGHT','NOT_OBSERVED','EXPIRED_UNPROVEN'),'historical':nullable(ref('SourceResult')),'current':ref('DemandCurrent'),'noEffectProven':{'type':'boolean'},'finality':nullable(ref('OwnerRef'))})
S['ReleaseTarget']=obj({'orderLineId':ref('Key'),'scheduleId':ref('Key'),'target':ref('SupplyTarget'),'expectedAcceptedSourceVersion':nullable(ref('Revision')),'previousResult':nullable(ref('OwnerRef'))})
S['ReleasePreviewInput']=obj({'orderVersion':ref('Key'),'targets':arr(ref('ReleaseTarget'),200,1)})
S['PlanRef']=obj({'planId':ref('Key'),'planVersion':ref('Revision'),'planDigest':ref('Digest')})
S['ApplyPlan']=obj({'plan':ref('PlanRef'),'reason':string(1000)})
S['OrderEffectPreviewInput']=obj({'editingVersion':ref('Key'),'scope':arr(ref('OrderRef'),200,1),'evidence':arr(ref('OwnerRef')),'reason':string(1000)})
S['OrderEffectPlan']=obj({'plan':ref('PlanRef'),'action':enum('CONFIRM','RELEASE','CHANGE','CANCEL','CLOSE'),'orderId':ref('Key'),'editingVersion':ref('Key'),'expectedEffectiveVersion':nullable(ref('Key')),'scope':arr(ref('OrderRef'),200,1),'sourceOperations':arr(ref('SourceOperation')),'conditions':arr(ref('Condition')),'ownerEvidence':arr(ref('OwnerRef')),'watermarks':arr(ref('FactWatermark')),'expiresAt':ref('Utc'),'canApply':{'type':'boolean'}})
S['CancelSlice']=obj({'order':ref('OrderRef'),'classification':enum('UNRELEASED','ACCEPTED','UNKNOWN'),'quantity':ref('Quantity'),'acceptedResult':nullable(ref('OwnerRef')),'shipmentWatermark':ref('FactWatermark')})
S['CancelPreviewInput']=obj({'editingVersion':ref('Key'),'slices':arr(ref('CancelSlice'),200,1),'reason':string(1000)})
S['ClosurePreviewInput']=obj({'orderVersion':ref('Key'),'scope':arr(ref('OrderRef'),200,1),'reason':enum('FULFILLED','CANCELLED','MIXED'),'obligations':arr(ref('Condition'),200,1),'responsibilityTransfers':arr(ref('OwnerRef'))})
S['ControlApplyInput']=obj({'control':ref('SourceControl'),'expectedControlSetRevision':ref('Revision')})
S['SourceCorrectionInput']=obj({'demandId':ref('Key'),'expectedDemandVersion':ref('Revision'),'source':ref('OrderRef'),'incorrectFields':arr(enum('QUANTITY','DATE','SITE','TECHNICAL_SCOPE'),4,1),'reason':string(1000)})
S['ReevaluateInput']=obj({'demandId':ref('Key'),'expectedDemandVersion':ref('Revision'),'expectedControlSetRevision':ref('Revision'),'reason':string(1000)})
S['ActivationMember']=obj({'releaseLineageId':ref('Key'),'demandId':ref('Key'),'expectedDemandVersion':ref('Revision'),'quantity':ref('Quantity'),'source':ref('OwnerRef')})
S['ActivationManifest']=obj({'schemaVersion':const('DEM-ACTIVATION/1'),'groupId':ref('Key'),'groupVersion':ref('Revision'),'retire':arr(ref('ActivationMember'),200,1),'activate':arr(ref('ActivationMember'),200,1),'transfers':arr(ref('OwnerRef'),200,1),'executionGateProofs':arr(ref('OwnerRef'),200,1),'approvalBindings':arr(ref('OwnerRef')),'manifestDigest':ref('Digest')})
S['ActivationApply']=obj({'manifest':ref('ActivationManifest'),'reason':string(1000)})
S['ActivationResult']=obj({'groupId':ref('Key'),'groupVersion':ref('Revision'),'manifestDigest':ref('Digest'),'decisionId':ref('Key'),'membershipRevision':ref('Revision'),'outcome':enum('ACTIVATED','ABORTED_WITH_FENCE'),'retired':arr(ref('ActivationMember')),'activated':arr(ref('ActivationMember')),'committedAt':ref('Utc')})
S['OrderSummary']=obj({'orderId':ref('Key'),'editingVersion':ref('Key'),'effectiveVersion':nullable(ref('Key')),'etag':string(512),'lifecycle':enum('DRAFT','DRAFT_CANCELLED','CONFIRMED','CLOSED'),'commercialCancelled':{'type':'boolean'},'sourceProtocol':enum('QTN_R2_H07','MAIN_INTAKE','DIRECT_EVIDENCED','EXTERNAL_AUTHORIZED','REPEAT_REFERENCE','LEGACY_OBSERVATION'),'sourceReceipt':nullable(ref('OwnerRef')),'missingConditions':arr(ref('Condition')),'asOf':ref('Utc')})
S['OrderPage']=obj({'items':arr(ref('OrderSummary'),100),'nextCursor':nullable(string(2048)),'asOf':ref('Utc')})
S['OrderDraftCreate']=obj({'sourceKind':enum('DIRECT_EVIDENCED','EXTERNAL_AUTHORIZED','REPEAT_REFERENCE'),'sourceIntentKey':ref('Key'),'sourceEvidence':arr(ref('OwnerRef')),'referenceOrder':nullable(ref('OwnerRef')),'salesOrganization':ref('Key'),'reason':string(1000)})
S['DraftLine']=obj({'orderLineId':nullable(ref('Key')),'displaySequence':{'type':'integer','minimum':1,'maximum':200},'lineKind':enum('GOODS','SERVICE'),'item':nullable(ref('OwnerRef')),'quantity':nullable(ref('Quantity')),'priceTerms':nullable(ref('OwnerRef')),'sourceLine':nullable(ref('OwnerRef'))})
S['DraftSchedule']=obj({'scheduleId':nullable(ref('Key')),'orderLineId':ref('Key'),'quantity':ref('Quantity'),'requiredDate':nullable(ref('BusinessDate')),'promisedDate':nullable(ref('BusinessDate')),'supplyTarget':nullable(ref('SupplyTarget'))})
S['OrderDraftSave']=obj({'editingVersion':ref('Key'),'soldTo':nullable(ref('OwnerRef')),'billTo':nullable(ref('OwnerRef')),'payer':nullable(ref('OwnerRef')),'currency':nullable(ref('OwnerRef')),'paymentTerms':nullable(ref('OwnerRef')),'deliveryTerms':nullable(ref('OwnerRef')),'shipTo':nullable(ref('OwnerRef')),'responsibleActor':nullable(ref('Key')),'customerOrderReference':nullable(string(256)),'lines':arr(ref('DraftLine')),'schedules':arr(ref('DraftSchedule')),'notes':nullable(string(2000))})
S['Money']=obj({'amount':ref('SignedDecimal'),'currency':ref('Key'),'policy':ref('OwnerRef')})
S['OriginLineMap']=obj({'sourceLine':ref('OwnerRef'),'orderLineId':ref('Key'),'scheduleId':nullable(ref('Key')),'quantity':ref('Quantity')})
S['SourceCheck']=obj({'state':enum('UNVERIFIED','VERIFIED','CONFLICT','UNAVAILABLE','REFERENCE_ONLY'),'reasons':arr(ref('Key')),'evidence':arr(ref('OwnerRef')),'watermark':ref('FactWatermark')})
S['Confirmation']=obj({'confirmationId':ref('Key'),'orderVersion':ref('Key'),'scope':arr(ref('OrderRef'),200,1),'confirmedAt':ref('Utc'),'actorId':ref('Key'),'evidence':arr(ref('OwnerRef'),200,1)})
S['DiscountTerms']=obj({'policy':ref('OwnerRef'),'kind':enum('NONE','RATE','AMOUNT'),'value':nullable(ref('Decimal')),'sequence':{'type':'integer','minimum':1,'maximum':20},'currency':nullable(ref('Key'))})
S['PriceTerms']=obj({'priceUom':ref('OwnerRef'),'priceBasisQty':ref('Decimal'),'unitPrice':ref('Decimal'),'currency':ref('OwnerRef'),'discounts':arr(ref('DiscountTerms'),20),'taxTreatment':ref('OwnerRef'),'roundingPolicy':ref('OwnerRef'),'zeroPriceReason':nullable(string(1000))})
S['QuantityDate']=obj({'quantity':ref('Quantity'),'date':ref('BusinessDate'),'source':ref('OwnerRef')})
S['ReplayEligibility']=obj({'allowed':enum('QUERY_ORIGINAL','REPAIR_PROJECTION','REPLAY_ORIGINAL','EXPLICIT_NEW_SUCCESSOR','NONE'),'evaluatedAt':ref('Utc'),'authorityEvidence':nullable(ref('OwnerRef')),'reasonCodes':arr(ref('Key'))})
S['ProjectionState']=obj({'owner':ref('Key'),'state':enum('CURRENT','PENDING','PARTIAL','CONFLICT','UNKNOWN'),'asOf':ref('Utc'),'originalFact':ref('OwnerRef')})
S['KnownQuantity']=obj({'knowledge':enum('KNOWN','UNKNOWN','STALE','RESTRICTED'),'quantity':nullable(ref('Quantity')),'watermark':ref('FactWatermark')})
S['BackorderReason']=obj({'code':ref('DictionaryValue'),'explanation':string(1000),'recordedAt':ref('Utc'),'owner':ref('Key')})
def owner_ref_for(contract, structure):
    return {'allOf':[ref('OwnerRef'),{'properties':{'contractId':{'const':contract}}}],'x-original-type':structure,'description':'Resolve complete original '+structure+' from exact '+contract+' binding before use; no arbitrary JSON payload.'}

# Read-model records for the non-command logical fields. Not a catch-all mutable body.
domain_fields={
11:enum('QUOTE_ACCEPTED','DIRECT_EVIDENCED','EXTERNAL_AUTHORIZED','REPEAT_REFERENCE'),12:ref('Key'),13:ref('Key'),14:string(256),15:ref('OwnerRef'),16:ref('Key'),17:ref('Key'),18:arr(ref('OwnerRef')),19:owner_ref_for('QTN-FACT-R2','AcceptanceFactHead.effectiveEvidence (QTN source); otherwise authenticated CustomerAcceptance VersionedEvidence'),20:arr(ref('OriginLineMap')),25:ref('SourceCheck'),27:string(256),34:ref('Key'),37:owner_ref_for('ORD-CON009','PriceBasisSnapshot'),42:ref('Condition'),43:ref('Confirmation'),44:ref('Key'),50:obj({'customerItemCode':string(256),'mapping':ref('OwnerRef')}),51:string(2000),52:ref('Quantity'),53:ref('Quantity'),54:ref('Quantity'),55:ref('Quantity'),59:ref('Decimal'),60:ref('Decimal'),61:ref('OwnerRef'),62:arr(ref('DiscountTerms'),20),63:ref('OwnerRef'),64:ref('Money'),65:arr(ref('OrderRef')),77:owner_ref_for('ORD-CON004','DateNodeDerivation'),79:ref('OwnerRef'),84:owner_ref_for('ORD-CON012','ActivationManifest'),85:arr(ref('OwnerRef')),86:owner_ref_for('ORD-CON004','AtpRequestScope'),87:ref('Key'),88:arr(ref('QuantityDate')),89:arr(string(200)),90:owner_ref_for('ORD-CON004','AtpResult / SelfExclusionProof'),91:owner_ref_for('ORD-CON004','AdoptionBinding'),92:owner_ref_for('ORD-CON003','ApplyIntent.approvalBinding'),93:owner_ref_for('ORD-CON003','ApprovalOutcome'),94:owner_ref_for('ORD-CON002','CreditDecision'),95:owner_ref_for('ORD-CON002','CreditDecision.creditException (or exact professional policy for its own action)'),102:ref('KnownQuantity'),103:obj({'value':ref('SignedDecimal'),'uom':ref('OwnerRef')}),105:ref('Key'),108:enum('NOT_SUBMITTED','LOCAL_REQUEST_SAVED','RECEIVED','BUSINESS_ACCEPTED','BUSINESS_REJECTED','UNKNOWN'),113:arr(owner_ref_for('ORD-CON006','DispositionResult')),114:ref('ReplayEligibility'),115:ref('ProjectionState'),137:owner_ref_for('ORD-R02-COMPOSITES','CommercialSupplyScopeMap'),116:arr(enum('PRICE','QUANTITY','DATE','SITE','TECHNICAL','CONTROL'),6,1),118:obj({'before':ref('OwnerRef'),'after':ref('OwnerRef'),'changedFields':arr(ref('Key')),'digest':ref('Digest')}),119:ref('Key'),120:owner_ref_for('ORD-CON006','DispositionResult'),121:owner_ref_for('ORD-CON006','ControlProof'),123:ref('Quantity'),124:arr(owner_ref_for('ORD-CON012','ActivationManifest.transfers')),125:ref('BackorderReason'),126:ref('KnownQuantity'),127:ref('KnownQuantity'),128:ref('KnownQuantity'),129:ref('KnownQuantity'),130:owner_ref_for('ORD-CON007','RunResultScope'),131:owner_ref_for('ORD-CON007','ReservationResult'),134:ref('Key'),135:string(256),136:owner_ref_for('ORD-R02-COMPOSITES','Import/ExportSnapshot'),140:owner_ref_for('ORD-R02-COMPOSITES','QuerySelection / ExportSelection')}
domain_names={11:'sourceType',12:'sourceOwner',13:'sourceSystem',14:'externalOrderNumber',15:'sourceObject',16:'sourceObjectVersion',17:'sourceIntentKey',18:'sourceEvidenceRefs',19:'customerAcceptanceRef',20:'originLineMap',25:'sourceCheckResult',27:'orderNumber',34:'partySnapshotVersion',37:'pricingPolicyRef',42:'customerRequiredAcceptance',43:'commercialConfirmation',44:'changeId',50:'customerItemRef',51:'itemDescriptionSnapshot',52:'originalCommercialQty',53:'approvedIncreaseQty',54:'effectiveDecreaseQty',55:'currentCommercialQty',59:'unitPrice',60:'priceBasisQty',61:'priceUom',62:'discountTerms',63:'taxTreatmentRef',64:'lineNetAmount',65:'priceChangeScope',77:'transitDerivation',79:'shipToSnapshot',84:'deliveryConstraintGroup',85:'predecessorSuccessorRefs',86:'atpRequestSnapshot',87:'atpEvaluationId',88:'atpSegments',89:'atpLimitations',90:'atpValidity',91:'promiseBasis',92:'approvalBinding',93:'approvalResult',94:'creditDecisionRef',95:'exceptionDecisionRef',102:'previousAcceptedQty',103:'targetDelta',105:'releaseBatchId',108:'processingState',113:'dispositionItems',114:'replayEligibility',115:'projectionSyncState',137:'commercialSupplyScopeMap',116:'changeClass',118:'changeDiff',119:'impactAssessmentVersion',120:'ownerDecision',121:'controlProof',123:'effectiveCancelQty',124:'transferOutInMap',125:'backorderReason',126:'effectiveShipmentQty',127:'effectiveDeliveredQty',128:'actualReturnQty',129:'remainingSupplyResponsibility',130:'coverageResultRef',131:'actualReservationRef',134:'importBatchKey',135:'importRowKey',136:'exportSnapshotRef',140:'exportSelection'}
S['OrderDomainFields']=obj({domain_names[n]:nullable(schema) for n,schema in domain_fields.items()},[])
S['OrderDomainFields']['properties']['customerAcceptanceRef']=nullable({'oneOf':[owner_ref_for('QTN-FACT-R2','AcceptanceFactHead.effectiveEvidence'),owner_ref_for('ORD-R02-COMPOSITES','VersionedEvidence for this direct customer acceptance')]})
S['OrderDomainFields']['properties']['exceptionDecisionRef']=nullable({'oneOf':[owner_ref_for('ORD-CON002','CreditDecision.creditException'),owner_ref_for('ORD-CON011','ConditionSet professional exception'),owner_ref_for('ORD-CON003','ApprovalOutcome bounded Sales exception')]})
S['OrderDetailQuery']=obj({'orderVersion':ref('Key'),'orderLineId':nullable(ref('Key')),'scheduleId':nullable(ref('Key')),'fields':arr(enum(*domain_names.values()),70,1)})
S['OrderDomainRead']=obj({'order':ref('OrderReadScope'),'fields':ref('OrderDomainFields'),'redactedFields':arr(ref('Key')),'watermarks':arr(ref('FactWatermark')),'asOf':ref('Utc')})
S['DraftLine']['properties']['priceTerms']=nullable(ref('PriceTerms'))

S['DemandRead']=obj({'demandId':ref('Key'),'etag':string(512),'controlSetRevision':ref('Revision'),'current':ref('DemandCurrent'),'asOf':ref('Utc')})
S['DemandCommandQuery']=obj({'operation':enum('DEM_CONTROL','DEM_SOURCE_CORRECTION','DEM_REEVALUATE'),'commandKey':ref('Key')})
S['ActivationQuery']={'oneOf':[obj({'kind':const('GROUP'),'groupId':ref('Key'),'groupVersion':ref('Revision'),'manifestDigest':ref('Digest')}),obj({'kind':const('COMMAND'),'operation':enum('DEM_ACTIVATE','DEM_ABORT'),'commandKey':ref('Key')})]}
S['ActivationResult']['properties']['fence']=nullable(ref('OwnerRef'))
S['ActivationResult']['required'].append('fence')
S['ActivationResult']['allOf']=[{'if':{'properties':{'outcome':{'const':'ABORTED_WITH_FENCE'}}},'then':{'properties':{'fence':ref('OwnerRef'),'retired':{'maxItems':0},'activated':{'maxItems':0}}},'else':{'properties':{'fence':{'type':'null'}}}}]
S['ActivationRead']=obj({'knowledge':enum('FOUND','IN_FLIGHT','NOT_OBSERVED','UNKNOWN'),'result':nullable(ref('ActivationResult')),'currentApplicability':enum('CURRENT','HISTORICAL_ONLY','RESTRICTED','UNKNOWN'),'asOf':ref('Utc')})
S['ActivationRead']['allOf']=[{'if':{'properties':{'knowledge':{'const':'FOUND'}}},'then':{'properties':{'result':ref('ActivationResult')}},'else':{'properties':{'result':{'type':'null'}}}}]
S['ActivationAbort']=obj({'manifest':ref('ActivationManifest'),'reason':string(1000)})
S['ActivationFence']=obj({'schemaVersion':const('DEM-ACTIVATION-FENCE/1'),'groupId':ref('Key'),'groupVersion':ref('Revision'),'manifestDigest':ref('Digest'),'decisionId':ref('Key'),'prohibitedOperations':{'const':['ACTIVATE','PREPARE_SUCCESSOR']},'closedAt':ref('Utc')})
S['ActivationFence']['properties']['tenantId']=ref('Key')
S['ActivationFence']['required'].append('tenantId')

paths={}
def endpoint(op, method, path, request, response, capability, write_mode=False, cas=False, scope='ENTITY_APPLY'):
    params=[]
    for name in re.findall(r'{([^}]+)}',path): params.append({'name':name,'in':'path','required':True,'schema':ref('Key')})
    if write_mode: params.append({'name':'Idempotency-Key','in':'header','required':True,'schema':ref('Key')})
    if cas: params.append({'name':'If-Match','in':'header','required':True,'schema':string(512)})
    body={'operationId':op,'x-capability':capability,'x-outcome-scope':scope,'x-design-status':'CURRENT_TASK_SUPPLEMENT_NOT_DEPLOYED','parameters':params,'responses':{'200':{'description':'Typed result; historical and current applicability remain separate.','content':{'application/json':{'schema':ref(response)}}}}}
    if request: body['requestBody']={'required':True,'content':{'application/json':{'schema':ref(request)}}}
    for code in ['400','401','403','404','409','412','413','422','428','429','503']:
        body['responses'][code]={'description':'Defined in supplement error table; never infer original unknown operation did not execute.','content':{'application/problem+json':{'schema':ref('Error')}}}
    paths.setdefault(path,{})[method.lower()]=body

A='/api/crm/v2/accounts'
endpoint('A01','POST',A+'/query','PageQuery','AccountPage','crm-account:query')
endpoint('A02','POST',A+'/duplicate-candidates/query','CandidateQuery','CandidateRead','crm-account:add + candidate.read')
endpoint('A03','POST',A,'AccountCreate','CommandReceipt','crm-account:add',True)
endpoint('A04','PATCH',A+'/{accountId}','AccountPatch','CommandReceipt','crm-account:edit; assignment fields require account.assign',True,True)
endpoint('A05','POST',A+'/{accountId}/deactivate','Reason','CommandReceipt','crm-account:edit + account.deactivate',True,True)
endpoint('A06','POST',A+'/{accountId}/restore','Reason','CommandReceipt','account.restore',True,True)
endpoint('A07','GET',A+'/{accountId}',None,'AccountRead','crm-account:query')
endpoint('A08','POST',A+'/{accountId}/sources/query','SourceQuery','SourceRead','account.source.read + source owner current disclosure')
endpoint('A09','POST','/internal/crm/v2/account-source-proofs','SourceProofNotice','ProjectionReceipt','service:account.source.ingest',False,False,'PROJECTION')
endpoint('A10','POST',A+'/{accountId}/main-navigation',None,'NavigationRead','account.navigate + target authorization')
endpoint('A11','POST',A+'/command-results/query','ReceiptQuery','ReceiptRead','account.command.read; MAIN_REQUEST additionally original result scope')
endpoint('A12','POST','/internal/crm/v2/account-c03-results','LegacyResultNotice','ProjectionReceipt','service:account.c03.ingest',False,False,'PROJECTION')
endpoint('A13','POST','/internal/crm/v2/account-mapping-decisions','MappingNotice','ProjectionReceipt','service:account.mapping.ingest',False,False,'PROJECTION')
endpoint('ACC_MAPPING_QUERY','POST','/internal/main/v2/account-mapping-decisions/query','MappingQuery','MappingRead','service:account.mapping.read')
endpoint('ACC_ELIGIBILITY','POST',A+'/mapping-eligibility/query','MappingQuery','EligibilityRead','account.mapping.read')
O='/api/main/v1/sales-orders'
endpoint('ORD_QUERY','POST',O+'/query','PageQuery','OrderPage','sales.order.read')
endpoint('ORD_READ','GET',O+'/{orderId}',None,'OrderSummary','sales.order.read')
endpoint('ORD_DETAIL','POST',O+'/{orderId}/details/query','OrderDetailQuery','OrderDomainRead','sales.order.read + each requested owner/field disclosure')
endpoint('ORD_DRAFT_CREATE','POST',O,'OrderDraftCreate','CommandReceipt','sales.order.create',True)
endpoint('ORD_DRAFT_SAVE','PUT',O+'/{orderId}/draft','OrderDraftSave','CommandReceipt','sales.order.edit',True,True)
endpoint('ORD_DRAFT_CANCEL','POST',O+'/{orderId}/draft-cancellations','Reason','CommandReceipt','sales.order.draft.cancel + qtn I11 if QTN source',True,True)
for action,request in [('confirm','OrderEffectPreviewInput'),('release','ReleasePreviewInput'),('change','OrderEffectPreviewInput'),('cancel','CancelPreviewInput'),('close','ClosurePreviewInput')]:
    endpoint('ORD_'+action.upper()+'_PREVIEW','POST',O+'/{orderId}/'+action+'-plans',request,'OrderEffectPlan','sales.order.'+action)
    endpoint('ORD_'+action.upper(),'POST',O+'/{orderId}/'+action+'-applications','ApplyPlan','CommandReceipt','sales.order.'+action,True,True,'LOCAL_REQUEST' if action=='release' else 'ENTITY_APPLY')
endpoint('ORD_RESULT','POST',O+'/command-results/query','ReceiptLocator','CommandReceipt','sales.order.command.read')
D='/internal/main/v1/demand-sources'
endpoint('DEM_RECEIVE','POST',D+'/operations','SourceOperation','SourceResult','service:planning.source.receive',False)
endpoint('DEM_RESULT','POST',D+'/results/query','SourceResultQuery','SourceResultRead','service:planning.source.result.read')
endpoint('DEM_CONTROL','POST','/api/main/v1/demands/{demandId}/controls','ControlApplyInput','CommandReceipt','planning.control.own-reason',True,True)
endpoint('DEM_SOURCE_CORRECTION','POST','/api/main/v1/demands/{demandId}/source-correction-requests','SourceCorrectionInput','CommandReceipt','planning.source.correction.request',True,True,'LOCAL_REQUEST')
endpoint('DEM_REEVALUATE','POST','/api/main/v1/demands/{demandId}/reevaluations','ReevaluateInput','CommandReceipt','planning.reevaluate',True,True,'LOCAL_REQUEST')
endpoint('DEM_ACTIVATE','POST',D+'/activation-groups','ActivationApply','ActivationResult','service:planning.activation.apply',True)
endpoint('DEM_READ','GET','/api/main/v1/demands/{demandId}',None,'DemandRead','planning.demand.read + current source disclosure')
endpoint('DEM_COMMAND_RESULT','POST','/api/main/v1/demands/command-results/query','DemandCommandQuery','CommandReceipt','planning.command.read + original target disclosure')
endpoint('DEM_ACTIVATION_RESULT','POST',D+'/activation-groups/results/query','ActivationQuery','ActivationRead','service:planning.activation.result.read')
endpoint('DEM_ABORT','POST',D+'/activation-groups/abort','ActivationAbort','ActivationResult','service:planning.activation.abort',True)

document={'openapi':'3.1.0','info':{'title':'CP6 commercial physical contract supplement','version':'1.0.0','description':'New design contract, not a declaration of deployed endpoints or historical acceptance. See 02-commercial-physical-contracts.md.'},'security':[{'Bearer':[]}],'paths':paths,'components':{'securitySchemes':{'Bearer':{'type':'http','scheme':'bearer'}},'schemas':S}}
write('commercial-wire-1.0.0.openapi.json',document)
def h(domain,body):
    return hashlib.sha256((domain+'\n'+json.dumps(body,ensure_ascii=False,sort_keys=True,separators=(',',':'))).encode('utf-8')).hexdigest()
def demo_ref(contract,eid):
    return {'owner':'DEMO_ONLY','contractId':contract,'contractVersion':'1','evidenceId':eid,'evidenceVersion':'1','contentDigest':h('DEMO-NON-AUTHORITY',{'id':eid})}
candidate={'ownerMappingId':'DEMO-M2a','ownerMappingRevision':'7','businessPartnerKey':'0007/BP-A','boundAccountBusinessVersion':'3'}
candidate['candidateDigest']=h('ACC-MAPPING-CANDIDATE-1',candidate)
decision={'schemaVersion':'ACC-MAPPING-DECISION/1','scope':{'mappingAuthority':'DEMO-Main.BP','scopeIdentity':'DEMO-SCOPE-1','scopeEpoch':'DEMO-EPOCH-1','organizationId':'DEMO-T','legalEntityKey':'DEMO-L','accountId':'DEMO-A','mappingPurpose':'ACCOUNT_BUSINESS_PARTNER'},'decision':{'decisionId':'DEMO-D2','position':'2'},'basis':'SUCCESSOR','predecessor':{'decisionId':'DEMO-D1','position':'1','digest':h('DEMO-PREDECESSOR',{'id':'D1'})},'candidates':[candidate],'selectedCandidateDigest':candidate['candidateDigest'],'decisionKind':'SELECT','coveredCandidateDigests':[candidate['candidateDigest']],'coveredConflictDigests':[],'completeAtWatermark':'DEMO-CUT-2','coversEntireScope':True,'effectiveFrom':'2026-10-11T00:00:00Z','effectiveUntil':None,'identityImpactPolicy':demo_ref('ACC-IDENTITY-IMPACT','binding-fields')}
decision['decision']['digest']=h('ACC-MAPPING-DECISION-1',decision)
decision['authorityProof']=demo_ref('ACC-MAPPING-AUTHORITY-PROOF','DEMO-NOT-A-SIGNATURE')
example_query={'kind':'MAIN_REQUEST','authority':'DEMO-Main.C03','accountId':'DEMO-A','requestId':'DEMO-Q1','requestVersion':'1'}
write('examples-1.0.0.json',{'fixture_only':True,'warning':'Static documentation values, not signed Owner evidence. Production verifier MUST reject DEMO identity. No business fixture executed.','examples':[{'schema':'AccountCreate','body':{'displayName':'文档示例账户','departmentId':'DEMO-D','accountType':None,'domains':['https://www.example.invalid/a'],'region':None}},{'schema':'ReceiptQuery','body':example_query},{'schema':'MappingNotice','body':{'eventId':'DEMO-E2','decision':decision}},{'schema':'CommandReceipt','body':{'locator':{'operation':'ORD_CONFIRM','commandKey':'DEMO-K1'},'receiptId':None,'outcome':'NOT_OBSERVED','scope':'ENTITY_APPLY','targetId':None,'targetVersion':None,'completedAt':None,'canOpen':False,'replayed':False,'noEffectProven':False,'finalityEvidence':None}}]})

source_specs={
 'ACC-OPS':('ab/ab181510eef18279__OPS-ACCOUNT.md',[(1,80)]),
 'ACC-RECOVERY':('be/bed32d5ca99a24a2__RECEIPT-MAPPING.md',[(1,60)]),
 'ACC-UA':('a3/a3e4af65be952562__ACCEPTANCE.md',[(1,40)]),
 'ACC-CARDS':('bd/bd97dd191196cc6a__CONTRACT-CARDS.md',[(1,96)]),
 'ACC-PAGES':('80/8029ab7aa847c9d1__PAGES-DELTA.md',[(1,52)]),
 'ACC-RULES':('c5/c5f63cf5d7f27627__RULES-RECOVERY.md',[(95,115)]),
 'ACC-INDEPENDENT':('11/11aeb11f6a3b717b__CRM-ACC-01_客户关系账户_前后端开发Spec_v1.0_DRAFT.md',[(180,217),(583,642),(918,962)]),
 'BP-R3':('92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md',[(1008,1040)]),
 'ORD-R2':('50/5096115db2722db7__ORD_R02.md',[(1,23),(79,101),(278,310),(2100,2285)]),
 'ORD-R3':('24/24d39db15ecc6288__ORD_R03.md',[(101,150),(152,253),(255,304),(305,355),(357,407),(428,453),(460,559),(562,611),(631,644),(664,711)]),
 'ORD-PB01':('61/6174494a5420f95a__CP6_PROGRESS_PB01.json',[(56,73)]),
 'QTN-R2':('45/45ac9a34dec6e8e4__ERP-QTN-01_正式报价与客户接受_前后端开发Spec_v1.0.3_REVIEW_R2_CANDIDATE.md',[(322,322),(348,374),(475,550),(652,664),(681,725),(745,782),(910,927)]),
 'ACC-FIELDS':('05/05f59b2151c73245__FIELDS-ACTIONS.md',[(1,42)]),
 'HO':('af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md',[(397,412),(704,722)]),
 'LEAD-CP22':('23/23151de8bc435698__CRM-LEAD-01_CP22_FULL-FIVE-SPEC-REVIEW-CANDIDATE.md',[(238,275)])}
evidence=[]
for key,(relative,ranges) in source_specs.items():
    path=ORIGINALS/relative
    evidence.append({'id':key,'source_path':path.as_posix(),'sha256':sha(path),'reading_method':'direct selected line reads; original scripts treated only as data','selected_ranges':[{'line':a,'end_line':b} for a,b in ranges]})
write('evidence-reading.json',{'version':VERSION,'scope':'Primary passages only; not a full original package or current code audit.','sources':evidence})

bindings=[]
def binding(key,owner,source,lines,types,rule):
    e=next(x for x in evidence if x['id']==source)
    bindings.append({'binding_id':key,'owner':owner,'source_path':e['source_path'],'sha256':e['sha256'],'line':lines[0],'end_line':lines[1],'exact_types_or_contracts':types,'consumer_requirement':rule,'adoption_status':'UNPROVEN','payload_policy':'Resolve reference to complete immutable original under current access; verify full type and native digest, retain original bytes and verifier/trust. Reference alone is never a permit.'})
binding('QTN-H07-R2','Main.QTN','QTN-R2',(348,374),['QuoteOrderSource','OrderSourceLine','OrderDraftReceipt','OrderAllocation'],'I10 reads full original; persists exact ranges, signed roundingAdjustment, Fact snapshot and customerEvidence. No new H/C ledger.')
binding('QTN-FACT-R2','Main.QTN','QTN-R2',(496,539),['AcceptanceFactHead','AcceptanceCorrectionRecord','AcceptanceFactImpact','AcceptanceFactCurrentRead','AcceptanceImpactReceipt'],'I19 asynchronous projection plus synchronous Fact participant before every new Sales effect; never update old source snapshot.')
binding('QTN-I11-R2','Main.QTN','QTN-R2',(702,710),['OrderSourceCorrectionInput','OrderSourceCorrectionProof','OrderSourceCorrectionReceipt'],'Call only in actual cancellation DbTransaction; full original allocation, never confirmed, no Demand/inventory/unknown effects.')
binding('ACC-C03-V1','Main.C03','ACC-CARDS',(26,38),['ErpEventContracts.cs@c778a3052a4416b82facb07b1244fc98fb6ad8a1'],'Native BusinessPartnerKey case retained; proof reference resolver uses exact archived native decoder. No inferred current qualification.')
binding('ACC-SEVEN-FIELD-DRAFT','Main.BP','ACC-INDEPENDENT',(925,951),['POST /internal/crm/v1/account-erp-mappings'],'Exactly eventId/accountId/legalEntityKey/mappingRevision/state/businessPartnerKey/sourceAsOf. Separate passive projection only.')
binding('LEAD-H02-CP22','CRM.LEAD','LEAD-CP22',(238,275),['AccountChoice','CreateAccountRequest','ATOMIC_SHARED_TRANSACTION'],'Keep exact native create body and native X-Command-Id adapter; owner identity mapped from conversionId/participant kind/clientRef, no HTTP self-call.')
binding('ORD-CON005','Main.Sales/Main.Planning','ORD-R3',(305,351),['SourceOperation','SourceControl','OperationResult','CurrentEffectivity','CoverageAndFinality'],'Physical successor is ORD-DEM-SOURCE/1. No source integer comparison across different lineage/tenant.')
binding('ORD-CON012','Main.Planning','ORD-R3',(664,709),['ActivationManifest','PrepareMemberResult','EffectiveMembershipDecision','ExecutionGateProof'],'New physical successor requires local authoritative membership gate, immutable full manifest and real execution fences.')
binding('ORD-CON002','Main.BP/Main.Finance','ORD-R3',(152,198),['TradingPartyScope','CreditCheckInput','CreditDecision','ExposureCommitment','PrepaymentCoverage','CreditReleaseDecision'],'Confirm/release/change bind actual roles, action, money/currency and exposure stage. CHECK_ONLY never substitutes COMMITMENT_REQUIRED.')
binding('ORD-CON003','OA/Main.Sales','ORD-R3',(204,249),['ApplyIntent','ApprovalOutcome','ParticipantGuarantee','ApplicationOutcome','AbortFinality'],'Persist exact operation/version/scope/digest and participants. Sales commitment requires current guarantees or a proved local shared transaction.')
binding('ORD-CON004','Main.Planning/Logistics/Main.Sales','ORD-R3',(255,299),['AtpRequestScope','DateNodeDerivation','AtpResult','AdoptionBinding','SelfExclusionProof'],'Only exact own Demand excluded; evaluation does not reserve or release; adoption binds current editing version and all relevant watermarks.')
binding('ORD-R02-COMPOSITES','Respective original Owner','ORD-R2',(288,308),['TypedScope','VersionedEvidence','Condition','FactWatermark','Conversion','PriceTerms','TransitDerivation','ApprovalBinding','CommercialSupplyScopeMap','RequestedControl','BusinessResult','ImpactItem','Transfer','CancelSlice','ClosureEvidence','QuerySelection','ExportSelection','Import/ExportSnapshot'],'Exact composite member lists at the named original range. Import/export remain external-owner capability, no new W1 import/export mutation.')
binding('ORD-CON006','WMS/PUR/MES/QA/PLM','ORD-R3',(357,404),['ImpactScopeManifest','ControlRequest','ControlProof','DispositionResult','HandoffAcceptance','ClosureAssessment'],'Action/entry/key coverage and current in-flight results mandatory; no arbitrary owner can release another reason.')
binding('ORD-CON007','Main.Planning/WMS','ORD-R3',(428,453),['CoverageRequest','SupplyOccurrence','RunResultScope','ReservationResult','CoveragePresentation'],'Exact input Demand versions, supply stage and watermark; Reservation contains Allocation; stale run never current.')
binding('ORD-CON008','WMS/Shipping','ORD-R3',(460,505),['FulfillmentFact','ResponsibilityMap','DeliveryEvidence','CorrectionAuthority','PostClosureReview'],'Fact ID/version independent from OrderVersion; snapshot/delta explicit, ordinary returns never correction.')
binding('ORD-CON009','Main.Sales/Main.Finance','ORD-R3',(511,556),['BillingSourceScope','PriceBasisSnapshot','BillingResult','SettlementAllocation','CommercialFinanceDelta'],'Price and finance references resolve complete original scope/value/units/policy records. No client totals or invented invoice/settlement status.')
binding('ORD-CON010','Main.Sales/CRM.OPP','ORD-R3',(562,607),['CommercialFact','ContributionMap','OpportunityProgress','CurrencyView','ProjectionReceipt'],'Use downstream accepted OPP-COMMERCIAL/1 exact consumer, no second Won or contribution writer in Sales/HO.')
binding('ORD-CON011','PLM','ORD-R3',(631,644),['ActivityAssessmentRequest','ConditionSet','EngineeringChangeNotice','BusinessImpactResponse','AdoptionProof'],'Purpose and activity-specific original proof required; no free-form approval JSON or generic READY.')
for contract,stype in [('ACC-MAPPING-AUTHORITY-PROOF','MappingAuthorityProof'),('ACC-IDENTITY-IMPACT','IdentityImpactPolicy')]:
    path=HERE/'commercial-wire-1.0.0.openapi.json'
    bindings.append({'binding_id':contract,'owner':'New supplement Main.BP / CRM.Account contract','source_path':path.as_posix(),'sha256':sha(path),'json_pointer':'/components/schemas/'+stype,'exact_types_or_contracts':[stype],'consumer_requirement':'Exact newly authored schema; actual registered producer/key and host adoption remain UNPROVEN. Historical protocols are unchanged.','adoption_status':'UNPROVEN','payload_policy':'Full exact original, current authorized verifier and tenant/scope binding mandatory; no client self-issued proof.'})
bindings.append({'binding_id':'DEM-ACTIVATION-FENCE','owner':'Main.Planning','source_path':(HERE/'commercial-wire-1.0.0.openapi.json').as_posix(),'sha256':sha(HERE/'commercial-wire-1.0.0.openapi.json'),'line':1,'end_line':1,'exact_types_or_contracts':['#/components/schemas/ActivationFence'],'consumer_requirement':'New R1 binding only: contractId DEM-ACTIVATION-FENCE, contractVersion 1, evidenceId decisionId, evidenceVersion groupVersion; contentDigest SHA256(DEM-ACTIVATION-FENCE-1 + LF + sorted compact UTF8 complete body). Verify trusted tenantId and exact group/version/manifest; immutable resolver keeps full body and denies cross-tenant queries. Never add permission to activate.','adoption_status':'UNPROVEN','payload_policy':'Read full immutable fence from exact registered Main.Planning resolver under current scope/read permission; no same-named foreign Ref'})
write('commercial-owner-bindings-1.0.0.json',{'version':VERSION,'bindings':bindings,'external_owner_policy':'OwnerRef.contractId MUST match the field/action allowlist in the supplement. No arbitrary owner JSON or client signed approval. Exact external schemas are imported by named original type, not silently redefined.'})

# Every original field is retained. This is an explicit physical disposition map, not 140 columns.
rows=[]
raw=(ORIGINALS/source_specs['ORD-R2'][0]).read_text(encoding='utf-8').splitlines()
special={1:('TRUSTED_CONTEXT','authentication.organizationId'),2:('TRUSTED_CONTEXT','authorization.objectScope'),3:('WIRE','headers.Idempotency-Key / SourceOperation.operationKey'),4:('WIRE','headers.If-Match'),5:('PERSISTED','Command.CanonicalBodyDigest / SourceOperation.sourceDigest'),6:('TRANSPORT','headers.X-Correlation-Id'),7:('WIRE','FactWatermark'),8:('PERSISTED','Audit.actorId/delegation/occurredAt'),9:('WIRE','Condition'),10:('READ_MODEL','OrderSummary.lifecycle'),139:('WIRE','PageQuery'),
21:('SUCCESSOR_ADAPTER','QTN-H07-R2; no ONE_SHOT/LIMITED_QUANTITY enum invented'),22:('SUCCESSOR_ADAPTER','QTN QuoteVersionRef + OrderSourceLine.quoteLineId/ranges'),23:('SUCCESSOR_ADAPTER','QTN OrderAllocation; PREPARED OrderIntent is not a claim'),24:('SUCCESSOR_ADAPTER','same QTN OrderAllocation/OrderDraftReceipt; Confirm produces no second QTN use'),
26:('WIRE','OrderSummary.orderId'),28:('WIRE','OrderSummary.effectiveVersion'),29:('WIRE','OrderSummary.editingVersion'),30:('WIRE','OrderDraftCreate.salesOrganization'),31:('WIRE','OrderDraftSave.soldTo'),32:('WIRE','OrderDraftSave.billTo'),33:('WIRE','OrderDraftSave.payer'),35:('WIRE','OrderDraftSave.currency'),36:('WIRE','OrderDraftSave.paymentTerms'),38:('WIRE','OrderDraftSave.deliveryTerms'),39:('WIRE','OrderDraftSave.shipTo'),40:('WIRE','OrderDraftSave.responsibleActor'),41:('WIRE','OrderDraftSave.customerOrderReference'),45:('WIRE','OrderDraftSave.notes'),46:('WIRE','DraftLine.orderLineId'),47:('WIRE','DraftLine.displaySequence'),48:('WIRE','DraftLine.lineKind'),49:('WIRE','DraftLine.item'),56:('WIRE','Quantity.uom'),57:('WIRE','SupplyTarget.standardQuantity.uom'),58:('WIRE','SupplyTarget.conversion'),66:('WIRE','DraftSchedule.scheduleId'),67:('WIRE','DraftSchedule.orderLineId'),68:('WIRE','DraftSchedule.quantity'),69:('WIRE','DraftSchedule.requiredDate'),70:('WIRE','BusinessDate.node'),71:('WIRE','DraftSchedule.promisedDate'),72:('WIRE','BusinessDate.node'),73:('WIRE','SupplyTarget.requiredDate'),74:('WIRE','BusinessDate.node'),75:('WIRE','BusinessDate.calendar'),76:('WIRE','BusinessDate.timeZone'),78:('WIRE','SupplyTarget.site'),80:('WIRE','SupplyTarget.supplyMode'),81:('WIRE','TechnicalScope'),82:('WIRE','TechnicalScope.applicability'),83:('WIRE','SupplyTarget.purposeEvidence'),96:('WIRE','SourceOperation.salesReleaseId'),97:('WIRE','SourceOperation.salesReleaseLineId'),98:('WIRE','SourceOperation.releaseLineageId'),99:('WIRE','SourceOperation.sourceVersion'),100:('WIRE','SourceOperation.expectedAcceptedSourceVersion'),101:('WIRE','SourceOperation.target.transactionQuantity'),104:('WIRE','SourceOperation.target.standardQuantity'),106:('WIRE','SourceOperation.activationManifest'),107:('WIRE','SourceControl'),109:('WIRE','SourceResultRead'),110:('WIRE','SourceResult.demandId'),111:('WIRE','SourceResult.demandVersion'),112:('WIRE','DemandCurrent'),117:('WIRE','OrderEffectPreviewInput.scope'),122:('WIRE','CancelSlice.quantity'),132:('WIRE','ClosurePreviewInput.reason'),133:('WIRE','ClosurePreviewInput.obligations'),138:('WIRE','CancelPreviewInput.slices')}
owner_groups=[(11,20,'ORD-R02 §§3.1/10.3; exact SourceEvidence/OriginLineMap, QTN-H07-R2 for quote source'),(27,44,'ORD-R02 order field dictionary + CON002/003; immutable Sales head and exact Owner evidence'),(50,65,'ORD-R02 §10.3 PriceTerms/Conversion; QTN OrderSourceLine exact net/tax/ranges for quote source'),(77,95,'ORD-R02 §10.3 TransitDerivation/ApprovalBinding and CON004/011; read original owner records'),(102,115,'ORD-CON005/006/007; immutable source/result/control and original execution facts'),(116,140,'ORD-R02 §10.3 ChangeDiff/ImpactItem/ClosureEvidence/ImportExportSnapshot; CON006/008/009/010')]
for line_no,line in enumerate(raw,1):
    m=re.match(r'\| <a id="(OD-R02-FLD-(\d+))".*?\| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \|',line)
    if not m: continue
    fid,n,label,stage,initial,owner,edit,rule=m.groups(); n=int(n)
    disposition,target=special.get(n,('OWNER_RECORD',next((t for a,b,t in owner_groups if a<=n<=b),'Sales immutable history')))
    if n in domain_fields:
        disposition='TYPED_READ_MODEL_AND_ORIGINAL_OWNER'
        target='commercial-wire-1.0.0.openapi.json#/components/schemas/OrderDomainFields/properties/'+domain_names[n]
    rows.append({'field_id':fid,'logical_name':label.split('/')[0].strip(),'logical_type':label.split('/',1)[1].strip() if '/' in label else '', 'required_at':stage.strip(),'owner':owner.strip(),'editing_rule':edit.strip(),'business_rule':rule.strip(),'physical_disposition':disposition,'target':target,'source_line':line_no,'source_sha256':sha(ORIGINALS/source_specs['ORD-R2'][0]),'runtime_status':'NOT_IMPLEMENTED'})
assert len(rows)==140 and len({r['field_id'] for r in rows})==140
write('ord-dem-fields-1.0.0.json',{'version':VERSION,'rule':'140 logical fields are distributed to exact wire members, trusted context, immutable records and external owner records. OWNER_RECORD means retain named original structure and consumer requirement; never accept arbitrary JSON or create a new Owner authority. Import/export and external specialized APIs are not new endpoints in W1.','fields':rows})

scenarios=[
 ('W1-ACC-01','A03 response lost; account now inactive, write right removed but RQ retained','Original receipt returned safely; one Account, one command, no second quota debit.'),
 ('W1-ACC-02','Same command key targets another account or actor','Conflict or neutral deny under current RQ; no alias bypass.'),
 ('W1-ACC-03','BP seven-field LINKED revision14 arrives for R03 account','History/passive projection only; cannot manufacture scope decision, bound account version or Effective.'),
 ('W1-ACC-04','M2a/M2b share predecessor M1; owner has not selected','WAIT_OWNER_RESOLUTION; valid M1 retained only while current proof and local eligibility valid.'),
 ('W1-ACC-05','Same scope/epoch/position contains contrary signed decision','Scope CONFLICT; no new use until complete owner resolution covers both digests.'),
 ('W1-ACC-06','A13 races deactivate or identity-relevant update','Conditional commit detects local stamp or later change invalidates use; never reactivates account.'),
 ('W1-ACC-07','Local account current, Main changes mapping after projection','New commercial use synchronously checks Main current mapping and BP; cached eligibility is not permit.'),
 ('W1-ACC-08','H02 source notice arrives without original Manifest or account target','WAITING evidence, zero Account/Lead/Contact/Opportunity creation.'),
 ('W1-ORD-01','QTN Q24 commits; HTTP response lost','Q25 returns original OrderDraft/Fact/Allocation; same Intent cannot make another Order.'),
 ('W1-ORD-02','Confirm QTN-sourced draft','Current Fact and Sales/BP/Finance/approval gates checked; no H-to-C call and no second allocation.'),
 ('W1-ORD-03','QTN allocated draft quantity changes','No in-place source quantity edit; original draft permanently cancelled under I11 proof, then explicit new Intent, or changes stay blocked.'),
 ('W1-ORD-04','Original evidence A/Fact1 corrected to B/Fact3','Old command returns A/Fact1 bytes; new Intent uses B/Fact3. Reuse key with B conflicts.'),
 ('W1-ORD-05','Fact becomes UNPROVEN before delayed impact arrives','Synchronous source guard blocks new Sales effect; old orders/Confirm/Allocation retained.'),
 ('W1-ORD-06','Two distinct responsibility groups share Opportunity','Distinct owner-approved source slots can create two Orders; same slot alias still one. No Opportunity-wide uniqueness.'),
 ('W1-DEM-01','Target30 revised40 on same lineage','Current target40; delta10; not total70. One current Demand.'),
 ('W1-DEM-02','v1 accepted; v2 rejected; v3 expected accepted v1','Accepted only with v2 actual terminal proof; v2 UNKNOWN blocks ordinary v3.'),
 ('W1-DEM-03','Cancel/control arrives before initial release','Source control exists without Demand ID; delayed old release denied by same gate.'),
 ('W1-DEM-04','Demand100 but coverage60','Receive target100; independent coverage60/shortage40; no silent partial acceptance.'),
 ('W1-DEM-05','A100 shipped30; activate B40+C30; C unknown','Prepared B/C excluded; no double-effective70+70. Atomic activation only when all proofs current.'),
 ('W1-DEM-06','Cancel evaluated at shipped40, actual shipped50 before commit','Old plan rejected stale; original60 cancellation not applied.'),
 ('W1-DEM-07','Planning reason removed but QA hold retained','Only owned reason removed; action still blocked by QA.'),
 ('W1-DEM-08','Source result not found after timeout','noEffectProven=false; query/replay original only, no new key or scan-create.'),
 ('W1-MIG-01','Legacy order unknown Confirm or source allocation','Retain legacy snapshot; no invented Fact/allocation/source evidence; dependent mutation refused.'),
 ('W1-STATIC-01','Parse OpenAPI and inspect local schema references, 13 ACC operations, 140 fields','All refs resolve, closed objects, every required member exists, original field IDs unique.')]
scenarios.extend([
 ('W1-DEM-READ-01','Authorized current Demand read then same-version own control','Return atomic ETag/controlSetRevision; current final authorization and If-Match required at mutation'),
 ('W1-DEM-READ-02','Control changes between read and mutation or read permission revoked','412 stale ETag or 403/hidden404; no new business effect'),
 ('W1-DEM-RECOVER-01','Lost manual command or activation response, query original key','Return exact original receipt/group decision; UNKNOWN does not activate or prove no effect'),
 ('W1-DEM-RECOVER-02','Wrong operation/manifest or unauthorized original-target query','Reject without disclosure or mutation; do not guess a new command'),
 ('W1-DEM-ABORT-01','Abort wins same group transaction before activation','Permanent exact group fence; empty activated/retired; unchanged membership; late writers blocked'),
 ('W1-DEM-ABORT-02','Activation wins or abort commit unknown','Already committed cannot abort; unknown queries original key and preserves original facts')])
write('acceptance-1.0.0.json',{'version':VERSION,'cases':[{'id':i,'scenario':s,'expected':e,'verification_level':'STATIC_ASSET' if i=='W1-STATIC-01' else 'REQUIRED_RUNTIME','result':'PENDING' if i=='W1-STATIC-01' else 'NOT_RUN'} for i,s,e in scenarios]})

findings=[]
mapping={
 'COM-ACC-PHYSICAL-WIRE':(['CRM-ACC-01','CRM-LEAD-01','ERP-BP-01'],['ACC-OPS','ACC-RECOVERY','ACC-UA','ACC-CARDS','ACC-PAGES','ACC-RULES','ACC-FIELDS','ACC-INDEPENDENT','BP-R3','LEAD-CP22'],'New ACC v2 A01-A13 and ACC-MAPPING-DECISION/1; independent C03/seven-field preserved; complete scope/owner selection/account binding/current qualification required.'),
 'COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY':(['ERP-ORD-01','PLAN-DEM-01'],['ORD-R2','ORD-R3','ORD-PB01'],'New ORD-DEM-SOURCE/1, typed lifecycle plans, local receipt/lineage/control/membership identities; complete 140-field disposition; no historical physical approval invented.'),
 'COM-ORD-QTN-H07-SUCCESSOR':(['ERP-ORD-01','ERP-QTN-01'],['ORD-R2','ORD-R3','QTN-R2'],'Adopt exact QTN R2 I10/Allocation at actual DRAFT commit; Confirm consumes no second H/C account; immutable source quantities, bounded I11 full release.'),
 'COM-HO-OLD-FACTORY':(['CRM-OPP-01','CRM-HO-01','INT-COM-01','ERP-ORD-01'],['HO','QTN-R2'],'Replace Opportunity-wide uniqueness only in new single-writer adapter with exact source intent/responsibility identities; legacy remains observation and explicit migration gate.'),
 'COM-QTN-FACT-H07-R2':(['ERP-QTN-01','ERP-ORD-01','INT-COM-01'],['QTN-R2'],'Persist exact historical Fact/source/receipt; synchronously join current Fact before new Sales effects; I19 projections never cancel order/release allocation or assert customer revocation.')}
for fid,(targets,srcs,decision) in mapping.items():
    ev=[]
    for sid in srcs:
        e=next(x for x in evidence if x['id']==sid)
        for r in e['selected_ranges']: ev.append({'source_path':e['source_path'],'sha256':e['sha256'],**r})
    selected=[{'id':i,'scenario':s,'expected':e,'verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'} for i,s,e in scenarios if i.startswith('W1-ACC' if fid=='COM-ACC-PHYSICAL-WIRE' else 'W1-DEM' if fid=='COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY' else 'W1-ORD')]
    classification={'COM-ACC-PHYSICAL-WIRE':'DESIGN_GAP','COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY':'DESIGN_GAP','COM-HO-OLD-FACTORY':'SUPERSEDED','COM-ORD-QTN-H07-SUCCESSOR':'SUPERSEDED','COM-QTN-FACT-H07-R2':'ADOPTION_PENDING'}[fid]
    findings.append({'source_finding_ids':[fid],'classification':classification,'targets':targets,'decision':decision,'evidence':ev,'outputs':['remediation/02-commercial-physical-contracts.md','remediation/assets/commercial/commercial-wire-1.0.0.openapi.json','remediation/assets/commercial/commercial-owner-bindings-1.0.0.json','remediation/assets/commercial/ord-dem-fields-1.0.0.json'],'acceptance':selected,'design_status':'DESIGN_RESOLVED' if classification=='DESIGN_GAP' else 'SUPERSESSION_RECORDED' if classification=='SUPERSEDED' else 'BOUNDARY_RECORDED','actual_adoption_status':'UNPROVEN','implementation_status':'NOT_IMPLEMENTED','verification_status':'STATIC_ASSETS_PENDING; REQUIRED_RUNTIME_NOT_RUN'})
(BOOK/'remediation/workparts/commercial.json').write_text(json.dumps({'findings':findings,'assets':[{'path':p.relative_to(BOOK).as_posix(),'sha256':sha(p),'version':VERSION} for p in sorted(HERE.glob('*.json'))]},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'Generated {len(paths)} paths, {sum(len(p) for p in paths.values())} operations, {len(S)} schemas, 140 mapped fields, {len(findings)} finding decisions.')
