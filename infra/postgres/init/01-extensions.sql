-- Runs once when the dev/CI database volume is first created (docker-entrypoint-initdb.d).
-- pg_stat_statements needs superuser and shared_preload_libraries, so it lives here rather than
-- in application migrations. Managed PostgreSQL providers usually enable it in their console.
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
-- Separate database for integration tests so they never touch development data.
CREATE DATABASE kindling_test OWNER kindling;
