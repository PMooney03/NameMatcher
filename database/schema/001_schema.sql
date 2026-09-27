-- Name matcher schema.
-- PostgreSQL is the matching engine. The web app only calls the functions in 002_functions.sql.

CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE TABLE IF NOT EXISTS company (
    company_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_name varchar(300) NOT NULL,
    normalised_name varchar(300) NOT NULL DEFAULT '',
    active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Lookups used by the candidate filter. Soundex is a cheap pre-filter, not the final decision.
CREATE INDEX IF NOT EXISTS ix_company_normalised_name
    ON company (normalised_name);

CREATE INDEX IF NOT EXISTS ix_company_normalised_prefix
    ON company (left(normalised_name, 3));

CREATE INDEX IF NOT EXISTS ix_company_normalised_soundex
    ON company (soundex(normalised_name));
