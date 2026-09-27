-- Existing databases created before the active flag. Fresh installs already have the column.
ALTER TABLE company
    ADD COLUMN IF NOT EXISTS active boolean NOT NULL DEFAULT true;
