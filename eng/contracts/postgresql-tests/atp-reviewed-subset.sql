-- S01-10 FIXTURE ONLY: selected types/tables, not a business migration.
-- Source composed-14 SHA256 29cd39f1448bf998a377497413baab6ff884d1a3939771479d0625fce31df1e0

-- composed-14 line 32; schema renamed only.
CREATE DOMAIN exact_fixture.sha256_hex AS text
  CHECK (VALUE ~ '^[0-9a-f]{64}$');

-- composed-14 line 34; schema renamed only.
CREATE DOMAIN exact_fixture.stable_id AS text
  CHECK (VALUE ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$');

-- composed-14 line 48; schema renamed only.
CREATE DOMAIN exact_fixture.decimal_30_8 AS numeric(30,8)
  CHECK (VALUE >= 0);

-- composed-14 line 50; schema renamed only.
CREATE TYPE exact_fixture.evaluation_state AS ENUM
  ('ACCEPTED','SNAPSHOTS_BOUND','CALCULATED','COMPLETED','PARTIAL','UNAVAILABLE',
   'REJECTED','CONFLICT','UNEVALUATED_POLICY','UNEVALUATED_SUPPLY',
   'STALE','RECOVERY_REQUIRED','FAILED_FINAL');

-- composed-14 line 117; schema renamed only.
CREATE TABLE exact_fixture.evaluation (
  organization_id exact_fixture.stable_id NOT NULL,
  evaluation_id uuid NOT NULL,
  status exact_fixture.evaluation_state NOT NULL DEFAULT 'ACCEPTED',
  actor_id exact_fixture.stable_id NOT NULL,
  delegation_id exact_fixture.stable_id,
  delegation_key text GENERATED ALWAYS AS (coalesce(delegation_id,'')) STORED,
  site_id exact_fixture.stable_id NOT NULL,
  item_id exact_fixture.stable_id NOT NULL,
  revision_id exact_fixture.stable_id NOT NULL,
  usage_code exact_fixture.stable_id NOT NULL,
  request_key exact_fixture.stable_id NOT NULL,
  request_body_digest exact_fixture.sha256_hex NOT NULL,
  replay_envelope_digest exact_fixture.sha256_hex NOT NULL,
  requested_quantity exact_fixture.decimal_30_8 NOT NULL,
  result_uom varchar(128) CHECK (result_uom IS NULL OR result_uom !~ '[^A-Za-z0-9._:/-]' AND result_uom ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'),
  result_scale smallint CHECK (result_scale BETWEEN 0 AND 8),
  total_eligible_capacity exact_fixture.decimal_30_8,
  allocated_to_request exact_fixture.decimal_30_8,
  unavailable_quantity exact_fixture.decimal_30_8,
  evaluated_at timestamptz,
  valid_until timestamptz,
  public_self_digest exact_fixture.sha256_hex,
  public_payload_digest exact_fixture.sha256_hex,
  public_representation_digest exact_fixture.sha256_hex,
  protected_self_digest exact_fixture.sha256_hex,
  protected_payload_digest exact_fixture.sha256_hex,
  protected_representation_digest exact_fixture.sha256_hex,
  etag_version bigint NOT NULL DEFAULT 1 CHECK (etag_version BETWEEN 1 AND 9007199254740991),
  created_at timestamptz NOT NULL,
  terminal_at timestamptz,
  PRIMARY KEY (organization_id,evaluation_id),
  CHECK (request_body_digest<>replay_envelope_digest),
  CHECK (requested_quantity>0),
  CHECK (created_at=date_trunc('milliseconds',created_at)),
  CHECK (evaluated_at IS NULL OR evaluated_at=date_trunc('milliseconds',evaluated_at)),
  CHECK (valid_until IS NULL OR valid_until=date_trunc('milliseconds',valid_until)),
  CHECK (valid_until IS NULL OR evaluated_at IS NULL OR valid_until>evaluated_at),
  CHECK (
    (status IN ('ACCEPTED','SNAPSHOTS_BOUND','CALCULATED','REJECTED','CONFLICT',
                'UNEVALUATED_POLICY','UNEVALUATED_SUPPLY','STALE','RECOVERY_REQUIRED','FAILED_FINAL')
      AND terminal_at IS NULL)
    OR
    (status IN ('COMPLETED','PARTIAL','UNAVAILABLE') AND terminal_at IS NOT NULL
      AND result_uom IS NOT NULL AND result_scale IS NOT NULL
      AND total_eligible_capacity IS NOT NULL AND allocated_to_request IS NOT NULL
      AND unavailable_quantity IS NOT NULL AND evaluated_at IS NOT NULL AND valid_until IS NOT NULL
      AND public_self_digest IS NOT NULL AND public_payload_digest IS NOT NULL
      AND public_representation_digest IS NOT NULL
      AND protected_self_digest IS NOT NULL AND protected_payload_digest IS NOT NULL
      AND protected_representation_digest IS NOT NULL)
  )
);

-- composed-14 line 286; schema renamed only.
CREATE TABLE exact_fixture.segment (
  organization_id exact_fixture.stable_id NOT NULL,
  evaluation_id uuid NOT NULL,
  ordinal bigint NOT NULL CHECK (ordinal BETWEEN 1 AND 9007199254740991),
  business_available_date date NOT NULL,
  uom varchar(128) NOT NULL CHECK (uom IS NULL OR uom !~ '[^A-Za-z0-9._:/-]' AND uom ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'),
  scale smallint NOT NULL CHECK (scale BETWEEN 0 AND 8),
  eligible_capacity exact_fixture.decimal_30_8 NOT NULL,
  allocated_quantity exact_fixture.decimal_30_8 NOT NULL,
  cumulative_allocated exact_fixture.decimal_30_8 NOT NULL,
  priority_rank integer NOT NULL CHECK (priority_rank>=0),
  firm_rank integer NOT NULL CHECK (firm_rank>=0),
  supply_class_rank integer NOT NULL CHECK (supply_class_rank>=0),
  owner_type_rank integer NOT NULL CHECK (owner_type_rank>=0),
  eligibility_class exact_fixture.stable_id NOT NULL,
  limitation_class exact_fixture.stable_id NOT NULL,
  calendar_digest exact_fixture.sha256_hex NOT NULL,
  segment_digest exact_fixture.sha256_hex NOT NULL,
  PRIMARY KEY (organization_id,evaluation_id,ordinal),
  FOREIGN KEY (organization_id,evaluation_id) REFERENCES exact_fixture.evaluation ON DELETE RESTRICT,
  CHECK (allocated_quantity<=eligible_capacity)
);

-- composed-14 line 309; schema renamed only.
CREATE TABLE exact_fixture.basis (
  organization_id exact_fixture.stable_id NOT NULL,
  evaluation_id uuid NOT NULL,
  basis_id exact_fixture.stable_id NOT NULL,
  physical_lineage_id exact_fixture.stable_id NOT NULL,
  source_class exact_fixture.stable_id NOT NULL,
  owner_type exact_fixture.stable_id NOT NULL,
  owner_id exact_fixture.stable_id NOT NULL,
  owner_version exact_fixture.stable_id NOT NULL,
  representation_id exact_fixture.stable_id NOT NULL,
  representation_sequence bigint NOT NULL CHECK (representation_sequence BETWEEN 1 AND 9007199254740991),
  predecessor_representation_id exact_fixture.stable_id,
  replacement_proof_digest exact_fixture.sha256_hex,
  available_at timestamptz NOT NULL,
  business_available_date date NOT NULL,
  native_selected_quantity exact_fixture.decimal_30_8 NOT NULL,
  native_uom varchar(128) NOT NULL CHECK (native_uom IS NULL OR native_uom !~ '[^A-Za-z0-9._:/-]' AND native_uom ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'),
  converted_selected_quantity exact_fixture.decimal_30_8 NOT NULL,
  result_uom varchar(128) NOT NULL CHECK (result_uom IS NULL OR result_uom !~ '[^A-Za-z0-9._:/-]' AND result_uom ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'),
  result_scale smallint NOT NULL CHECK (result_scale BETWEEN 0 AND 8),
  reservation_deducted exact_fixture.decimal_30_8 NOT NULL,
  safety_stock_deducted exact_fixture.decimal_30_8 NOT NULL,
  eligible_capacity exact_fixture.decimal_30_8 NOT NULL,
  conversion_proof_digest exact_fixture.sha256_hex NOT NULL,
  calendar_proof_digest exact_fixture.sha256_hex NOT NULL,
  row_digest exact_fixture.sha256_hex NOT NULL,
  PRIMARY KEY (organization_id,evaluation_id,basis_id),
  FOREIGN KEY (organization_id,evaluation_id) REFERENCES exact_fixture.evaluation ON DELETE RESTRICT,
  CHECK ((representation_sequence=1 AND predecessor_representation_id IS NULL AND replacement_proof_digest IS NULL)
      OR (representation_sequence>1 AND predecessor_representation_id IS NOT NULL AND replacement_proof_digest IS NOT NULL)),
  CHECK (reservation_deducted+safety_stock_deducted+eligible_capacity=converted_selected_quantity)
);

-- composed-14 line 368; schema renamed only.
CREATE TABLE exact_fixture.reservation_ledger (
  organization_id exact_fixture.stable_id NOT NULL,
  evaluation_id uuid NOT NULL,
  reservation_id exact_fixture.stable_id NOT NULL,
  reservation_digest exact_fixture.sha256_hex NOT NULL,
  demand_owner exact_fixture.stable_id NOT NULL,
  demand_id exact_fixture.stable_id NOT NULL,
  demand_version exact_fixture.stable_id NOT NULL,
  line_id exact_fixture.stable_id NOT NULL,
  schedule_id exact_fixture.stable_id NOT NULL,
  site_id exact_fixture.stable_id NOT NULL,
  item_id exact_fixture.stable_id NOT NULL,
  revision_id exact_fixture.stable_id NOT NULL,
  usage_code exact_fixture.stable_id NOT NULL,
  uom varchar(128) NOT NULL CHECK (uom IS NULL OR uom !~ '[^A-Za-z0-9._:/-]' AND uom ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'),
  physical_lineage_id exact_fixture.stable_id NOT NULL,
  converted_quantity exact_fixture.decimal_30_8 NOT NULL,
  self_excluded boolean NOT NULL,
  deducted_quantity exact_fixture.decimal_30_8 NOT NULL,
  allocation_digest exact_fixture.sha256_hex NOT NULL,
  PRIMARY KEY (organization_id,evaluation_id,reservation_id),
  FOREIGN KEY (organization_id,evaluation_id) REFERENCES exact_fixture.evaluation ON DELETE RESTRICT,
  CHECK ((self_excluded AND deducted_quantity=0) OR (NOT self_excluded AND deducted_quantity=converted_quantity))
);

-- composed-14 line 197; schema renamed only.
CREATE TABLE exact_fixture.replay_record (
  organization_id exact_fixture.stable_id NOT NULL,
  actor_id exact_fixture.stable_id NOT NULL,
  delegation_id exact_fixture.stable_id,
  delegation_key text GENERATED ALWAYS AS (coalesce(delegation_id,'')) STORED,
  operation text NOT NULL CHECK (operation='ATP_EVALUATE'),
  request_key exact_fixture.stable_id NOT NULL,
  request_body_digest exact_fixture.sha256_hex NOT NULL,
  replay_envelope_digest exact_fixture.sha256_hex NOT NULL,
  evaluation_id uuid NOT NULL,
  replay_state text NOT NULL CHECK (replay_state IN ('BOUND','CALCULATED','TERMINAL','FAILED_FINAL')),
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL,
  PRIMARY KEY (organization_id,actor_id,delegation_key,operation,request_key),
  FOREIGN KEY (organization_id,evaluation_id) REFERENCES exact_fixture.evaluation ON DELETE RESTRICT,
  CHECK (request_body_digest<>replay_envelope_digest)
);
