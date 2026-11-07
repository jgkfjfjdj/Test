/**
 * Drizzle schema root. Tables are added module by module as milestones implement them
 * (see docs/blueprint/08-database.md for the full target schema).
 *
 * M0 contains no application tables yet: the first migration only installs the
 * extensions every later migration relies on (citext, pg_trgm).
 */
export {};
