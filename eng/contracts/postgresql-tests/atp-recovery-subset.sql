-- Executable lock/binding subset only. NOT the full recovery/publish/authorization implementation.
-- Pinned composed-14 includes finance-recovery/source-patches.json patches 0..4 (PLAN-IF-005).
-- Patches 0,1,2 acquire ordered replay rows before evaluation. The common critical section is below.
CREATE FUNCTION exact_fixture.lock_recovery_rows(p_organization_id text,p_evaluation_id uuid)
RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
  PERFORM 1 FROM exact_fixture.replay_record
   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id
   ORDER BY actor_id COLLATE "C",delegation_key COLLATE "C",operation COLLATE "C",request_key COLLATE "C" FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'REPLAY_BINDING_MISSING' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM exact_fixture.evaluation
   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'EVALUATION_BINDING_MISSING' USING ERRCODE='23514'; END IF;
END $$;

-- Patches 3,4 bind both digests and acquire replay FOR UPDATE before the evaluation lock.
CREATE FUNCTION exact_fixture.lock_replay_binding(p_organization_id text,p_actor_id text,p_delegation_id text,
  p_request_key text,p_body_digest text,p_replay_digest text)
RETURNS uuid LANGUAGE plpgsql SET search_path=pg_catalog AS $$
DECLARE bound_id uuid; result_state exact_fixture.evaluation_state;
BEGIN
  SELECT r.evaluation_id INTO bound_id FROM exact_fixture.replay_record r
   WHERE r.organization_id=p_organization_id AND r.actor_id=p_actor_id
     AND r.delegation_id IS NOT DISTINCT FROM p_delegation_id
     AND r.operation='ATP_EVALUATE' AND r.request_key=p_request_key
     AND r.request_body_digest=p_body_digest AND r.replay_envelope_digest=p_replay_digest
   FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'REPLAY_BINDING_MISSING' USING ERRCODE='23514'; END IF;
  SELECT e.status INTO STRICT result_state FROM exact_fixture.evaluation e
   WHERE e.organization_id=p_organization_id AND e.evaluation_id=bound_id FOR UPDATE;
  RETURN bound_id;
END $$;
