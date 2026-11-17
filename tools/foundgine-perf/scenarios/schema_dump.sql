-- Prints ONE JSON document describing the public schema (tables, columns, PK, unique, FK, indexes) plus the migration
-- history table each tool leaves behind. Names of constraints/indexes are deliberately NOT emitted: EF, Foundgine's SQL generator
-- and Flyway scripts name them differently, and that is not a difference in the resulting schema.
-- Run:  psql -At -f schema_dump.sql  (inside the arm's postgres container)
SELECT json_build_object(
  'columns', (SELECT coalesce(json_agg(json_build_array(table_name, column_name, data_type, is_nullable) ORDER BY table_name, column_name), '[]'::json)
              FROM information_schema.columns WHERE table_schema = 'public'
                AND table_name NOT IN ('__EFMigrationsHistory', 'flyway_schema_history')),
  'keys', (SELECT coalesce(json_agg(json_build_array(t, kind, cols) ORDER BY t, kind, cols::text), '[]'::json) FROM (
             SELECT c.conrelid::regclass::text AS t,
                    CASE c.contype WHEN 'p' THEN 'pk' WHEN 'u' THEN 'unique' END AS kind,
                    (SELECT json_agg(a.attname ORDER BY a.attname) FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)) AS cols
             FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
             WHERE n.nspname = 'public' AND c.contype IN ('p', 'u')
             UNION ALL
             SELECT i.indrelid::regclass::text, 'unique',
                    (SELECT json_agg(a.attname ORDER BY a.attname) FROM pg_attribute a WHERE a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey))
             FROM pg_index i JOIN pg_class ic ON ic.oid = i.indexrelid JOIN pg_namespace n ON n.oid = ic.relnamespace
             WHERE n.nspname = 'public' AND i.indisunique AND NOT i.indisprimary
               AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = i.indexrelid)
           ) k WHERE t NOT LIKE '%__EFMigrationsHistory%' AND t NOT LIKE '%flyway_schema_history%'),
  'fks', (SELECT coalesce(json_agg(json_build_array(c.conrelid::regclass::text, c.confrelid::regclass::text,
              (SELECT json_agg(a.attname ORDER BY a.attname) FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey))) ORDER BY 1, 2), '[]'::json)
          FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'public' AND c.contype = 'f'),
  'indexes', (SELECT coalesce(json_agg(json_build_array(i.indrelid::regclass::text, (SELECT json_agg(a.attname ORDER BY a.attname) FROM pg_attribute a WHERE a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey))) ORDER BY 1, 2::text), '[]'::json)
          FROM pg_index i JOIN pg_class ic ON ic.oid = i.indexrelid JOIN pg_namespace n ON n.oid = ic.relnamespace
          WHERE n.nspname = 'public' AND NOT i.indisprimary AND NOT i.indisunique
            AND i.indrelid::regclass::text NOT LIKE '%__EFMigrationsHistory%' AND i.indrelid::regclass::text NOT LIKE '%flyway_schema_history%'),
  'history', (CASE WHEN to_regclass('public."__EFMigrationsHistory"') IS NOT NULL
                   THEN (xpath('/row/j/text()', query_to_xml($q$SELECT json_build_object('kind', 'ef', 'applied', (SELECT coalesce(json_agg("MigrationId" ORDER BY "MigrationId"), '[]'::json) FROM public."__EFMigrationsHistory")) AS j$q$, false, true, '')))[1]::text::json
                   WHEN to_regclass('public.flyway_schema_history') IS NOT NULL
                   THEN (xpath('/row/j/text()', query_to_xml($q$SELECT json_build_object('kind', 'flyway', 'applied', (SELECT coalesce(json_agg(script ORDER BY installed_rank), '[]'::json) FROM public.flyway_schema_history WHERE success AND version IS NOT NULL)) AS j$q$, false, true, '')))[1]::text::json
                   ELSE json_build_object('kind', 'none', 'applied', '[]'::json) END)
)::text;
