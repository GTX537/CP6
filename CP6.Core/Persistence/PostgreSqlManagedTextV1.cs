using System.Text.RegularExpressions;

namespace CP6.Core.Persistence;

/// <summary>Versioned database functions used by the PostgreSQL v1 storage baseline.</summary>
public static class PostgreSqlManagedTextV1
{
    public static string CreateFunctions(string schema, string collation)
    {
        if (!Regex.IsMatch(schema, "\\A[A-Za-z_][A-Za-z_0-9]{0,62}\\z") ||
            !Regex.IsMatch(collation, "\\A[A-Za-z_][A-Za-z_0-9]{0,62}\\z"))
            throw new ArgumentException("Managed text identifiers must be single bounded identifiers.");
        return $$"""
            CREATE FUNCTION "{{schema}}".cp6_text_range_v1(value text, kind text)
            RETURNS boolean LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE AS $cp6$
            DECLARE scalar text;
            BEGIN
              IF kind NOT IN ('hex', 'alpha') THEN
                RAISE EXCEPTION 'Unsupported CP6 text range' USING ERRCODE='22023';
              END IF;
              IF octet_length(value)=0 THEN RETURN true; END IF;
              FOREACH scalar IN ARRAY regexp_split_to_array(value COLLATE "C", '') LOOP
                IF kind='hex' THEN
                  IF NOT ((scalar COLLATE "public"."{{collation}}" BETWEEN '0' AND '9')
                       OR (scalar COLLATE "public"."{{collation}}" BETWEEN 'a' AND 'f')) THEN
                    RETURN false;
                  END IF;
                ELSE
                  IF NOT (scalar COLLATE "public"."{{collation}}" BETWEEN 'A' AND 'Z') THEN RETURN false; END IF;
                END IF;
              END LOOP;
              RETURN true;
            END $cp6$;

            CREATE FUNCTION "{{schema}}".cp6_cp936_length_v1(value text)
            RETURNS integer LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE AS $cp6$
            DECLARE scalar text; capacity bigint := 0;
            BEGIN
              FOREACH scalar IN ARRAY regexp_split_to_array(value COLLATE "C", '') LOOP
                -- Windows CP936 assigns Euro the single byte 0x80.
                IF ascii(scalar)=8364 THEN capacity := capacity+1;
                ELSE
                  BEGIN
                    capacity := capacity+octet_length(convert_to(scalar,'GBK'));
                  EXCEPTION WHEN character_not_in_repertoire OR untranslatable_character THEN
                    -- The non-SC SQL Server codepage replaces each UTF-16 unit.
                    capacity := capacity+CASE WHEN ascii(scalar)>65535 THEN 2 ELSE 1 END;
                  END;
                END IF;
              END LOOP;
              IF capacity>2147483647 THEN RAISE EXCEPTION 'CP6 capacity overflow' USING ERRCODE='22003'; END IF;
              RETURN capacity::integer;
            END $cp6$;
            """;
    }
}
