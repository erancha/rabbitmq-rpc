-- Row count of each application table, with the last-50-rows dump kept commented out.
--   ./scripts/sql-helper.sh -f scripts/sql/show-all-tables.sql
--
-- The count(*) lines run; uncomment a SELECT * line to dump that table's 50 newest rows instead.
-- Add a block here when a new table is introduced.

\echo
\echo ==================== Users ====================
-- SELECT * FROM "Users" ORDER BY "Id" DESC LIMIT 50;
SELECT count(*) FROM "Users";

\echo
\echo ==================== TodoItems ====================
-- SELECT * FROM "TodoItems" ORDER BY "Id" DESC LIMIT 50;
SELECT count(*) FROM "TodoItems";

\echo
\echo ==================== ProcessedMessages ====================
-- SELECT * FROM "ProcessedMessages" ORDER BY "CreatedAt" DESC LIMIT 50;
SELECT count(*) FROM "ProcessedMessages";
