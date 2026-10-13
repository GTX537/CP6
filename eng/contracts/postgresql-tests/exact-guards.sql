-- Task-local fixture guards. Functions are SECURITY INVOKER, with a pinned pg_catalog search path.
CREATE FUNCTION exact_fixture.credit_integer_input(value text) RETURNS numeric
LANGUAGE plpgsql IMMUTABLE STRICT SET search_path=pg_catalog AS $$
BEGIN
  IF value ~ '[^0-9-]' OR value !~ '^(0|-?[1-9][0-9]{0,37})$' THEN
    RAISE EXCEPTION 'CREDIT_RAW_COEFFICIENT_INVALID' USING ERRCODE='23514';
  END IF;
  RETURN value::numeric;
END $$;

CREATE TABLE exact_fixture.credit (
  id text PRIMARY KEY,
  profile text NOT NULL CHECK (profile IN ('Signed','NonNegative','Positive')),
  coefficient numeric(38,0), scale smallint, nullable_value boolean NOT NULL DEFAULT false,
  CHECK ((nullable_value AND coefficient IS NULL AND scale IS NULL) OR
    (coefficient IS NOT NULL AND scale IS NOT NULL AND scale BETWEEN 0 AND 8
     AND coefficient BETWEEN -99999999999999999999999999999999999999 AND 99999999999999999999999999999999999999
     AND (coefficient<>0 OR scale=0) AND (scale=0 OR mod(coefficient,10)<>0)
     AND (profile='Signed' OR (profile='NonNegative' AND coefficient>=0) OR (profile='Positive' AND coefficient>0))))
);

CREATE FUNCTION exact_fixture.inventory_valid(digits text, numeric_scale smallint, wire text, profile text, nullable_value boolean, numeric_rule text DEFAULT 'Any')
RETURNS boolean LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
DECLARE magnitude text; expected_digits text; expected_scale integer; point integer; max_scale integer; negative boolean;
BEGIN
  IF profile NOT IN ('Decimal18','Qty12','NonNegativeQty12','PositiveInteger','MavgNumerator') OR profile IS NULL THEN RETURN false; END IF;
  IF numeric_rule NOT IN ('Any','NonNegative','Positive') OR numeric_rule IS NULL THEN RETURN false; END IF;
  IF digits IS NULL AND numeric_scale IS NULL AND wire IS NULL THEN RETURN nullable_value IS TRUE; END IF;
  IF digits IS NULL OR numeric_scale IS NULL OR wire IS NULL THEN RETURN false; END IF;
  IF wire ~ '[^0-9.-]' THEN RETURN false; END IF;
  negative := left(wire,1)='-';
  magnitude := CASE WHEN negative THEN substr(wire,2) ELSE wire END;
  expected_scale := 0;
  IF profile='PositiveInteger' THEN
    IF wire !~ '^[1-9][0-9]*$' THEN RETURN false; END IF;
  ELSIF profile='MavgNumerator' THEN
    IF wire !~ '^-?[0-9]+$' THEN RETURN false; END IF;
  ELSE
    IF wire !~ '^-?(0|[1-9][0-9]*)(\.[0-9]+)?$' THEN RETURN false; END IF;
    IF profile='NonNegativeQty12' AND negative THEN RETURN false; END IF;
    max_scale := CASE WHEN profile IN ('Qty12','NonNegativeQty12') THEN 12 ELSE 18 END;
    point := strpos(magnitude,'.');
    expected_scale := CASE WHEN point=0 THEN 0 ELSE length(magnitude)-point END;
    IF expected_scale>max_scale THEN RETURN false; END IF;
  END IF;
  expected_digits := ltrim(replace(magnitude,'.',''),'0');
  IF expected_digits='' THEN
    expected_digits := '0'; expected_scale := 0;
  ELSE
    WHILE expected_scale>0 AND right(expected_digits,1)='0' LOOP
      expected_digits := left(expected_digits,length(expected_digits)-1); expected_scale := expected_scale-1;
    END LOOP;
    IF negative THEN expected_digits := '-' || expected_digits; END IF;
  END IF;
  IF numeric_rule='NonNegative' AND left(expected_digits,1)='-' THEN RETURN false; END IF;
  IF numeric_rule='Positive' AND (left(expected_digits,1)='-' OR expected_digits='0') THEN RETURN false; END IF;
  RETURN digits=expected_digits AND numeric_scale=expected_scale;
END $$;

-- This guard receives text BEFORE varchar typmod coercion. Direct column writes alone cannot
-- distinguish an overlong trailing-space input that PostgreSQL has already truncated.
CREATE FUNCTION exact_fixture.uom_input(value text) RETURNS text
LANGUAGE plpgsql IMMUTABLE STRICT SET search_path=pg_catalog AS $$
BEGIN
  IF length(value) NOT BETWEEN 1 AND 128 OR value ~ '[^A-Za-z0-9._:/-]'
     OR value !~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$' THEN
    RAISE EXCEPTION 'ATP_RAW_UOM_INVALID' USING ERRCODE='23514';
  END IF;
  RETURN value;
END $$;

CREATE TABLE exact_fixture.inventory_scalar (
  id text PRIMARY KEY, profile text NOT NULL, digits text, scale smallint, wire text,
  CHECK (exact_fixture.inventory_valid(digits,scale,wire,profile,false))
);
CREATE TABLE exact_fixture.original_document (id text PRIMARY KEY, original_utf8 bytea NOT NULL);
