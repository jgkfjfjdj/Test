-- M0: extensions required by the platform schema (docs/blueprint/08-database.md §1).
-- Both are "trusted" extensions (PostgreSQL 13+), so the database owner can create them
-- without superuser rights. pg_stat_statements needs superuser + shared_preload_libraries
-- and is therefore installed by infrastructure (infra/postgres/init), not by migrations.
CREATE EXTENSION IF NOT EXISTS citext;
--> statement-breakpoint
CREATE EXTENSION IF NOT EXISTS pg_trgm;
