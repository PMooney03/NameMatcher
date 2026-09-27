-- Write path. The website and the browser viewer call these procedures.
-- Reading a grid stays on functions, because a procedure cannot return a table.

CREATE OR REPLACE PROCEDURE usp_add_company(
    IN p_company_name text,
    INOUT p_company_id integer DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_company_name IS NULL OR btrim(p_company_name) = '' THEN
        RAISE EXCEPTION 'Enter a company name.'
            USING ERRCODE = '22023';
    END IF;

    IF char_length(btrim(p_company_name)) > 300 THEN
        RAISE EXCEPTION 'Company name must be 300 characters or fewer.'
            USING ERRCODE = '22023';
    END IF;

    INSERT INTO company (company_name)
    VALUES (btrim(p_company_name))
    RETURNING company.company_id INTO p_company_id;
END;
$$;

CREATE OR REPLACE PROCEDURE usp_delete_company(IN p_company_id integer)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_company_id IS NULL THEN
        RAISE EXCEPTION 'Choose a company to delete.'
            USING ERRCODE = '22023';
    END IF;

    DELETE FROM company
    WHERE company_id = p_company_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'That company was not found.'
            USING ERRCODE = '22023';
    END IF;
END;
$$;

-- Hide or restore a company. The row stays. Search and groups skip inactive rows.
CREATE OR REPLACE PROCEDURE usp_set_company_active(
    IN p_company_id integer,
    IN p_active boolean
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_company_id IS NULL OR p_active IS NULL THEN
        RAISE EXCEPTION 'Choose a company and whether it is active.'
            USING ERRCODE = '22023';
    END IF;

    UPDATE company
       SET active = p_active
     WHERE company_id = p_company_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'That company was not found.'
            USING ERRCODE = '22023';
    END IF;
END;
$$;

-- Return type changed when active was added, so replace the old function.
DROP FUNCTION IF EXISTS fn_get_stored_company(integer);

-- The row as it is stored: original name plus the normalised name filled by the trigger.
CREATE OR REPLACE FUNCTION fn_get_stored_company(p_company_id integer)
RETURNS TABLE (
    company_id integer,
    company_name character varying,
    normalised_name character varying,
    active boolean,
    created_at timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        c.company_id,
        c.company_name,
        c.normalised_name,
        c.active,
        c.created_at
    FROM company AS c
    WHERE c.company_id = p_company_id;
$$;

DROP FUNCTION IF EXISTS fn_list_stored_companies();

CREATE OR REPLACE FUNCTION fn_list_stored_companies()
RETURNS TABLE (
    company_id integer,
    company_name character varying,
    normalised_name character varying,
    active boolean,
    created_at timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        c.company_id,
        c.company_name,
        c.normalised_name,
        c.active,
        c.created_at
    FROM company AS c
    ORDER BY c.active DESC, c.company_id DESC;
$$;
