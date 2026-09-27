-- Groups (who owns whom) and links (who has worked together).
-- Both procedures resolve names with fn_normalise_company_name, so the stored spelling is used.

ALTER TABLE company
    ADD COLUMN IF NOT EXISTS parent_company_id integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_company_parent'
    ) THEN
        ALTER TABLE company
            ADD CONSTRAINT fk_company_parent
            FOREIGN KEY (parent_company_id) REFERENCES company (company_id)
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS company_link (
    link_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id integer NOT NULL REFERENCES company (company_id) ON DELETE CASCADE,
    partner_company_id integer NOT NULL REFERENCES company (company_id) ON DELETE CASCADE,
    link_type varchar(40) NOT NULL,
    note varchar(300),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_company_link_not_self CHECK (company_id <> partner_company_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_company_link_pair
    ON company_link (company_id, partner_company_id, link_type);

CREATE OR REPLACE FUNCTION fn_require_stored_company(p_company_name text)
RETURNS integer
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    normalised text;
    found_id integer;
    found_count integer;
BEGIN
    normalised := fn_normalise_company_name(p_company_name);

    IF normalised IS NULL THEN
        RAISE EXCEPTION 'Enter a company name.'
            USING ERRCODE = '22023';
    END IF;

    SELECT count(*), min(c.company_id)
      INTO found_count, found_id
      FROM company AS c
     WHERE c.normalised_name = normalised;

    IF found_count = 0 THEN
        RAISE EXCEPTION 'No stored company matches "%". Add it first.', btrim(p_company_name)
            USING ERRCODE = '22023';
    END IF;

    IF found_count > 1 THEN
        RAISE EXCEPTION 'More than one stored company matches "%".', btrim(p_company_name)
            USING ERRCODE = '22023';
    END IF;

    RETURN found_id;
END;
$$;

CREATE OR REPLACE PROCEDURE usp_set_parent_company(
    IN p_company_name text,
    IN p_parent_name text
)
LANGUAGE plpgsql
AS $$
DECLARE
    child_id integer;
    parent_id integer;
BEGIN
    child_id := fn_require_stored_company(p_company_name);
    parent_id := fn_require_stored_company(p_parent_name);

    IF child_id = parent_id THEN
        RAISE EXCEPTION 'A company cannot be its own parent.'
            USING ERRCODE = '22023';
    END IF;

    UPDATE company
       SET parent_company_id = parent_id
     WHERE company_id = child_id;
END;
$$;

CREATE OR REPLACE PROCEDURE usp_add_company_link(
    IN p_company_name text,
    IN p_partner_name text,
    IN p_link_type text,
    IN p_note text DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
DECLARE
    left_id integer;
    right_id integer;
    kind text;
BEGIN
    left_id := fn_require_stored_company(p_company_name);
    right_id := fn_require_stored_company(p_partner_name);
    kind := upper(btrim(COALESCE(p_link_type, '')));

    IF left_id = right_id THEN
        RAISE EXCEPTION 'A company cannot be linked to itself.'
            USING ERRCODE = '22023';
    END IF;

    IF kind NOT IN ('CONTRACT', 'SUPPLIER', 'CLIENT') THEN
        RAISE EXCEPTION 'Link type must be contract, supplier, or client.'
            USING ERRCODE = '22023';
    END IF;

    INSERT INTO company_link (company_id, partner_company_id, link_type, note)
    VALUES (left_id, right_id, kind, NULLIF(btrim(p_note), ''))
    ON CONFLICT (company_id, partner_company_id, link_type) DO UPDATE
        SET note = EXCLUDED.note;
END;
$$;

-- Parent and the other active companies in the same one-level group.
CREATE OR REPLACE FUNCTION fn_group_summary(p_company_id integer)
RETURNS TABLE (
    company_id integer,
    parent_name character varying,
    group_with text
)
LANGUAGE sql
STABLE
AS $$
    WITH self AS (
        SELECT c.company_id, c.parent_company_id
        FROM company AS c
        WHERE c.company_id = p_company_id
    ),
    root AS (
        SELECT COALESCE(self.parent_company_id, self.company_id) AS root_id
        FROM self
    )
    SELECT
        self.company_id,
        parent.company_name,
        (
            SELECT string_agg(member.company_name, ', ' ORDER BY member.company_name)
            FROM company AS member
            CROSS JOIN root
            WHERE member.active
              AND member.company_id <> self.company_id
              AND member.company_id IS DISTINCT FROM self.parent_company_id
              AND (
                    member.company_id = root.root_id
                    OR member.parent_company_id = root.root_id
              )
        )
    FROM self
    CROSS JOIN root
    LEFT JOIN company AS parent
      ON parent.company_id = self.parent_company_id
     AND parent.active;
$$;

CREATE OR REPLACE FUNCTION fn_company_group(p_company_name text)
RETURNS TABLE (
    company_id integer,
    company_name character varying,
    normalised_name character varying,
    role text
)
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    target_id integer;
    root_id integer;
BEGIN
    target_id := fn_require_stored_company(p_company_name);

    SELECT COALESCE(c.parent_company_id, c.company_id)
      INTO root_id
      FROM company AS c
     WHERE c.company_id = target_id;

    RETURN QUERY
    SELECT
        c.company_id,
        c.company_name,
        c.normalised_name,
        CASE
            WHEN c.company_id = target_id THEN 'This company'
            WHEN c.company_id = root_id THEN 'Parent'
            ELSE 'In the group'
        END
    FROM company AS c
    WHERE (c.company_id = root_id OR c.parent_company_id = root_id)
      AND (c.active OR c.company_id = target_id)
    ORDER BY CASE WHEN c.company_id = root_id THEN 0 ELSE 1 END, c.company_name;
END;
$$;

CREATE OR REPLACE FUNCTION fn_company_links(p_company_name text)
RETURNS TABLE (
    company_name character varying,
    partner_name character varying,
    link_type character varying,
    note character varying
)
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    target_id integer;
BEGIN
    target_id := fn_require_stored_company(p_company_name);

    RETURN QUERY
    SELECT
        self.company_name,
        other.company_name,
        link.link_type,
        link.note
    FROM company_link AS link
    JOIN company AS self ON self.company_id = target_id
    JOIN company AS other
      ON other.company_id = CASE
            WHEN link.company_id = target_id THEN link.partner_company_id
            ELSE link.company_id
         END
    WHERE (link.company_id = target_id OR link.partner_company_id = target_id)
      AND other.active
    ORDER BY other.company_name;
END;
$$;

-- Example group and one contract. Safe to run more than once.
DO $$
DECLARE
    new_id integer;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM company WHERE normalised_name = 'ALPHABET') THEN
        CALL usp_add_company('Alphabet', new_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM company WHERE normalised_name = 'GOOGLE') THEN
        CALL usp_add_company('Google', new_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM company WHERE normalised_name = 'YOUTUBE') THEN
        CALL usp_add_company('YouTube', new_id);
    END IF;

    CALL usp_set_parent_company('Google', 'Alphabet');
    CALL usp_set_parent_company('YouTube', 'Alphabet');
    CALL usp_add_company_link(
        'Smith and Sons Building',
        'Acme Hardware Limited',
        'contract',
        'Fit-out of a hardware depot'
    );
END $$;
