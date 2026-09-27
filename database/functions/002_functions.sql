-- Matching functions.
-- Weights and phonetic bands are declared once so they are easy to change.

CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ---------------------------------------------------------------------------
-- Normalise a company name before comparison.
-- Example: "O'Reilly Technologies Limited" -> "OREILLY TECHNOLOGIES"
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_normalise_company_name(p_company_name text)
RETURNS text
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    value text;
    -- Whole trailing words only. COMPANY is listed as its own word so it is not confused with CO.
    suffix_pattern constant text := '(LIMITED|LTD|LLC|INC|PLC|COMPANY|CO)';
BEGIN
    IF p_company_name IS NULL THEN
        RETURN NULL;
    END IF;

    value := upper(p_company_name);

    -- "&" is the word AND, not punctuation.
    value := replace(value, '&', ' AND ');

    -- Apostrophes are spelling, not word breaks: O'Reilly and OReilly become OREILLY.
    value := replace(value, '''', '');
    value := replace(value, '’', '');
    value := replace(value, '`', '');

    -- Remove dots first so "Ltd." and "L.L.C." become LTD and LLC.
    value := replace(value, '.', '');

    value := regexp_replace(value, '[^A-Z0-9 ]', ' ', 'g');
    value := regexp_replace(value, '\s+', ' ', 'g');
    value := btrim(value);

    -- A leading initial belongs to the next word: "O RILEY" -> "ORILEY".
    -- The next word must be at least 4 letters so "R AND D" is left alone.
    value := regexp_replace(value, '^([A-Z]) ([A-Z0-9]{4,})', '\1\2');

    -- Drop legal suffixes from the end, repeatedly ("Limited Ltd" -> nothing extra).
    WHILE value ~ (' ' || suffix_pattern || '$') LOOP
        value := regexp_replace(value, ' ' || suffix_pattern || '$', '');
    END LOOP;

    RETURN NULLIF(value, '');
END;
$$;

-- Keeps stored normalised_name aligned with fn_normalise_company_name.
CREATE OR REPLACE FUNCTION trg_set_normalised_name()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.normalised_name := fn_normalise_company_name(NEW.company_name);

    IF NEW.normalised_name IS NULL THEN
        RAISE EXCEPTION 'Company name "%" has nothing left to compare after normalisation', NEW.company_name;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS company_set_normalised_name ON company;

CREATE TRIGGER company_set_normalised_name
    BEFORE INSERT OR UPDATE OF company_name
    ON company
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_normalised_name();

-- ---------------------------------------------------------------------------
-- Levenshtein distance: insertions, deletions and substitutions.
-- Classic dynamic programming. prev[] is the previous row of the matrix.
-- Empty string distance is the other string's length. NULL in, NULL out.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_levenshtein_distance(p_string1 text, p_string2 text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    len1 integer;
    len2 integer;
    i integer;
    j integer;
    substitution_cost integer;
    prev integer[];
    curr integer[];
BEGIN
    IF p_string1 IS NULL OR p_string2 IS NULL THEN
        RETURN NULL;
    END IF;

    len1 := char_length(p_string1);
    len2 := char_length(p_string2);

    IF len1 = 0 THEN
        RETURN len2;
    ELSIF len2 = 0 THEN
        RETURN len1;
    END IF;

    -- Column j lives at index j + 1 because PostgreSQL arrays are 1-based.
    prev := ARRAY[]::integer[];
    FOR j IN 0..len2 LOOP
        prev := prev || j;
    END LOOP;

    FOR i IN 1..len1 LOOP
        curr := ARRAY[i];

        FOR j IN 1..len2 LOOP
            IF substring(p_string1 FROM i FOR 1) = substring(p_string2 FROM j FOR 1) THEN
                substitution_cost := 0;
            ELSE
                substitution_cost := 1;
            END IF;

            curr := curr || LEAST(
                curr[j] + 1,                  -- insert
                prev[j + 1] + 1,              -- delete
                prev[j] + substitution_cost   -- substitute
            );
        END LOOP;

        prev := curr;
    END LOOP;

    RETURN prev[len2 + 1];
END;
$$;

-- 100 = identical, 0 = nothing in common. NULL or a NULL partner scores 0.
CREATE OR REPLACE FUNCTION fn_levenshtein_similarity(p_string1 text, p_string2 text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    distance integer;
    max_len integer;
BEGIN
    IF p_string1 IS NULL OR p_string2 IS NULL THEN
        RETURN 0;
    END IF;

    IF p_string1 = p_string2 THEN
        RETURN 100;
    END IF;

    max_len := GREATEST(char_length(p_string1), char_length(p_string2));
    IF max_len = 0 THEN
        RETURN 100;
    END IF;

    distance := fn_levenshtein_distance(p_string1, p_string2);

    RETURN round((1 - (distance::numeric / max_len)) * 100)::integer;
END;
$$;

-- ---------------------------------------------------------------------------
-- Token pairs. Each search word is paired with its closest unused candidate
-- word. Leftover candidate words are returned with no search word so the
-- detail page can show why an extra word lowered the score.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_explain_token_pairs(p_search text, p_candidate text)
RETURNS TABLE (
    search_word text,
    candidate_word text,
    word_score integer
)
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    search_words text[];
    candidate_words text[];
    used boolean[];
    search_count integer;
    candidate_count integer;
    i integer;
    j integer;
    best_score integer;
    best_index integer;
    current_score integer;
BEGIN
    IF p_search IS NULL OR p_candidate IS NULL
       OR btrim(p_search) = '' OR btrim(p_candidate) = '' THEN
        RETURN;
    END IF;

    SELECT coalesce(array_agg(word ORDER BY word), ARRAY[]::text[])
      INTO search_words
      FROM regexp_split_to_table(p_search, '\s+') AS word
     WHERE char_length(word) >= 2;

    SELECT coalesce(array_agg(word ORDER BY word), ARRAY[]::text[])
      INTO candidate_words
      FROM regexp_split_to_table(p_candidate, '\s+') AS word
     WHERE char_length(word) >= 2;

    search_count := coalesce(array_length(search_words, 1), 0);
    candidate_count := coalesce(array_length(candidate_words, 1), 0);

    IF search_count = 0 AND candidate_count = 0 THEN
        RETURN;
    END IF;

    used := array_fill(false, ARRAY[GREATEST(candidate_count, 1)]);

    FOR i IN 1..search_count LOOP
        best_score := -1;
        best_index := 0;

        FOR j IN 1..candidate_count LOOP
            IF NOT used[j] THEN
                current_score := fn_levenshtein_similarity(search_words[i], candidate_words[j]);
                IF current_score > best_score THEN
                    best_score := current_score;
                    best_index := j;
                END IF;
            END IF;
        END LOOP;

        search_word := search_words[i];
        IF best_index > 0 THEN
            used[best_index] := true;
            candidate_word := candidate_words[best_index];
            word_score := best_score;
        ELSE
            candidate_word := NULL;
            word_score := 0;
        END IF;
        RETURN NEXT;
    END LOOP;

    FOR j IN 1..candidate_count LOOP
        IF NOT used[j] THEN
            search_word := NULL;
            candidate_word := candidate_words[j];
            word_score := 0;
            RETURN NEXT;
        END IF;
    END LOOP;
END;
$$;

-- Sum of the paired word scores, divided by the longer word list.
CREATE OR REPLACE FUNCTION fn_token_similarity(p_search text, p_candidate text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    search_count integer;
    candidate_count integer;
    total numeric;
BEGIN
    IF p_search IS NULL OR p_candidate IS NULL
       OR btrim(p_search) = '' OR btrim(p_candidate) = '' THEN
        RETURN 0;
    END IF;

    IF p_search = p_candidate THEN
        RETURN 100;
    END IF;

    SELECT
        count(*) FILTER (WHERE pair.search_word IS NOT NULL),
        count(*) FILTER (WHERE pair.candidate_word IS NOT NULL),
        coalesce(sum(pair.word_score) FILTER (WHERE pair.search_word IS NOT NULL), 0)
    INTO search_count, candidate_count, total
    FROM fn_explain_token_pairs(p_search, p_candidate) AS pair;

    IF search_count = 0 OR candidate_count = 0 THEN
        RETURN 0;
    END IF;

    RETURN round(total / GREATEST(search_count, candidate_count))::integer;
END;
$$;

-- Trigram similarity from pg_trgm, scaled to 0-100. Diagnostic only: not in the final blend.
CREATE OR REPLACE FUNCTION fn_trigram_score(p_search text, p_candidate text)
RETURNS integer
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE
        WHEN p_search IS NULL OR p_candidate IS NULL
          OR btrim(p_search) = '' OR btrim(p_candidate) = '' THEN 0
        ELSE round(similarity(p_search, p_candidate) * 100)::integer
    END;
$$;

-- difference() is 0-4. Soundex is a hint, mapped onto 0-100 for the blend below.
-- 4 identical code, 3 strong, 2 weak, 0-1 low. Do not treat this as proof of a match.
CREATE OR REPLACE FUNCTION fn_phonetic_score(p_string1 text, p_string2 text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    phonetic_difference integer;
BEGIN
    IF p_string1 IS NULL OR p_string2 IS NULL
       OR btrim(p_string1) = '' OR btrim(p_string2) = '' THEN
        RETURN 0;
    END IF;

    phonetic_difference := difference(p_string1, p_string2);

    RETURN CASE phonetic_difference
        WHEN 4 THEN 100
        WHEN 3 THEN 75
        WHEN 2 THEN 40
        WHEN 1 THEN 15
        ELSE 0
    END;
END;
$$;

-- Shared significant word, length >= 4, so short tokens like CO or AND do not qualify.
CREATE OR REPLACE FUNCTION fn_has_shared_word(p_search text, p_candidate text)
RETURNS boolean
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM regexp_split_to_table(p_search, '\s+') AS search_word(word)
        JOIN regexp_split_to_table(p_candidate, '\s+') AS candidate_word(word)
          ON search_word.word = candidate_word.word
        WHERE char_length(search_word.word) >= 4
    );
$$;

-- Cheap gate before Levenshtein. A name stays if ANY test passes, so obvious
-- spelling variants are not dropped just because one signal missed them.
CREATE OR REPLACE FUNCTION fn_is_match_candidate(p_search text, p_candidate text)
RETURNS boolean
LANGUAGE plpgsql
IMMUTABLE
AS $$
BEGIN
    IF p_search IS NULL OR p_candidate IS NULL OR p_search = '' OR p_candidate = '' THEN
        RETURN false;
    END IF;

    -- Soundex difference of 2+ means the codes still share something.
    -- Stephen / Steven / Stefen pass. Names with an unrelated opening sound do not.
    IF difference(p_search, p_candidate) >= 2 THEN
        RETURN true;
    END IF;

    -- Same first three letters. Catches edits that Soundex happens to miss.
    IF char_length(p_search) >= 3
       AND char_length(p_candidate) >= 3
       AND left(p_search, 3) = left(p_candidate, 3) THEN
        RETURN true;
    END IF;

    -- Shared word such as ENGINEERING or MOONEY, even when the other word differs.
    IF fn_has_shared_word(p_search, p_candidate) THEN
        RETURN true;
    END IF;

    -- Similar length and the same first two letters. First letter alone is too wide
    -- once the table holds more than a handful of names.
    IF char_length(p_search) >= 2
       AND char_length(p_candidate) >= 2
       AND left(p_search, 2) = left(p_candidate, 2)
       AND abs(char_length(p_search) - char_length(p_candidate))
           <= GREATEST(2, char_length(p_search) / 5) THEN
        RETURN true;
    END IF;

    -- Trigram overlap catches reordered or partial names the tests above can miss.
    -- 0.30 is deliberately low: this only decides who gets scored, not the final rank.
    IF similarity(p_search, p_candidate) >= 0.30 THEN
        RETURN true;
    END IF;

    RETURN false;
END;
$$;

-- Scores one already-normalised pair.
-- Exact normalised match is 100.
-- Otherwise: 45% full-string Levenshtein, 30% token Levenshtein, 25% phonetic.
-- Trigram is returned for inspection and is not part of that blend.
DROP FUNCTION IF EXISTS fn_score_normalised_names(text, text);

CREATE OR REPLACE FUNCTION fn_score_normalised_names(p_search text, p_candidate text)
RETURNS TABLE (
    exact_match_score integer,
    soundex_score integer,
    difference_score integer,
    levenshtein_distance integer,
    levenshtein_score integer,
    token_score integer,
    trigram_score integer,
    final_score integer,
    is_exact_normalised_match boolean,
    is_phonetic_match boolean
)
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    -- Change these three numbers to retune the blend. They should add up to 1.
    full_string_weight constant numeric := 0.45;
    token_weight constant numeric := 0.30;
    phonetic_weight constant numeric := 0.25;
    raw_difference integer;
BEGIN
    IF p_search IS NULL OR p_candidate IS NULL THEN
        exact_match_score := 0;
        soundex_score := 0;
        difference_score := 0;
        levenshtein_distance := 0;
        levenshtein_score := 0;
        token_score := 0;
        trigram_score := 0;
        final_score := 0;
        is_exact_normalised_match := false;
        is_phonetic_match := false;
        RETURN NEXT;
        RETURN;
    END IF;

    is_exact_normalised_match := p_search = p_candidate;
    exact_match_score := CASE WHEN is_exact_normalised_match THEN 100 ELSE 0 END;

    raw_difference := COALESCE(difference(p_search, p_candidate), 0);
    difference_score := raw_difference;
    soundex_score := fn_phonetic_score(p_search, p_candidate);
    is_phonetic_match := raw_difference >= 3;

    levenshtein_distance := COALESCE(fn_levenshtein_distance(p_search, p_candidate), 0);
    levenshtein_score := fn_levenshtein_similarity(p_search, p_candidate);
    token_score := fn_token_similarity(p_search, p_candidate);
    trigram_score := fn_trigram_score(p_search, p_candidate);

    IF is_exact_normalised_match THEN
        final_score := 100;
    ELSE
        final_score := round(
            (levenshtein_score * full_string_weight)
            + (token_score * token_weight)
            + (soundex_score * phonetic_weight)
        )::integer;
    END IF;

    RETURN NEXT;
END;
$$;

-- Main search. Normalises the input, drops unlikely rows, then scores the rest.
DROP FUNCTION IF EXISTS find_company_matches(text, integer, integer);

CREATE OR REPLACE FUNCTION find_company_matches(
    p_company_name text,
    p_minimum_score integer DEFAULT 50,
    p_max_results integer DEFAULT 20
)
RETURNS TABLE (
    company_id integer,
    company_name character varying,
    normalised_name character varying,
    normalised_search text,
    exact_match_score integer,
    soundex_score integer,
    difference_score integer,
    levenshtein_distance integer,
    levenshtein_score integer,
    token_score integer,
    trigram_score integer,
    final_score integer,
    is_exact_normalised_match boolean,
    is_phonetic_match boolean
)
LANGUAGE plpgsql
STABLE
AS $$
#variable_conflict use_column
DECLARE
    search_name text;
    minimum_score integer;
    max_results integer;
BEGIN
    search_name := fn_normalise_company_name(p_company_name);
    minimum_score := LEAST(100, GREATEST(0, COALESCE(p_minimum_score, 50)));
    max_results := LEAST(500, GREATEST(1, COALESCE(p_max_results, 20)));

    IF search_name IS NULL THEN
        RETURN;
    END IF;

    RETURN QUERY
    WITH candidates AS (
        SELECT
            c.company_id,
            c.company_name,
            c.normalised_name
        FROM company AS c
        WHERE c.active
          AND fn_is_match_candidate(search_name, c.normalised_name)
    ),
    scored AS (
        SELECT
            c.company_id,
            c.company_name,
            c.normalised_name,
            s.exact_match_score,
            s.soundex_score,
            s.difference_score,
            s.levenshtein_distance,
            s.levenshtein_score,
            s.token_score,
            s.trigram_score,
            s.final_score,
            s.is_exact_normalised_match,
            s.is_phonetic_match
        FROM candidates AS c
        CROSS JOIN LATERAL fn_score_normalised_names(search_name, c.normalised_name) AS s
    )
    SELECT
        scored.company_id,
        scored.company_name,
        scored.normalised_name,
        search_name,
        scored.exact_match_score,
        scored.soundex_score,
        scored.difference_score,
        scored.levenshtein_distance,
        scored.levenshtein_score,
        scored.token_score,
        scored.trigram_score,
        scored.final_score,
        scored.is_exact_normalised_match,
        scored.is_phonetic_match
    FROM scored
    WHERE scored.final_score >= minimum_score
    ORDER BY scored.final_score DESC, scored.levenshtein_score DESC, scored.token_score DESC, scored.company_name
    LIMIT max_results;
END;
$$;

-- One company, always scored, including rows the candidate filter would skip.
-- The detail page uses this so the explanation comes from SQL.
DROP FUNCTION IF EXISTS explain_company_match(text, integer);

CREATE OR REPLACE FUNCTION explain_company_match(
    p_company_name text,
    p_company_id integer
)
RETURNS TABLE (
    company_id integer,
    company_name character varying,
    normalised_name character varying,
    normalised_search text,
    exact_match_score integer,
    soundex_score integer,
    difference_score integer,
    levenshtein_distance integer,
    levenshtein_score integer,
    token_score integer,
    trigram_score integer,
    final_score integer,
    is_exact_normalised_match boolean,
    is_phonetic_match boolean
)
LANGUAGE plpgsql
STABLE
AS $$
#variable_conflict use_column
DECLARE
    search_name text;
BEGIN
    search_name := fn_normalise_company_name(p_company_name);

    IF search_name IS NULL OR p_company_id IS NULL THEN
        RETURN;
    END IF;

    RETURN QUERY
    SELECT
        c.company_id,
        c.company_name,
        c.normalised_name,
        search_name,
        s.exact_match_score,
        s.soundex_score,
        s.difference_score,
        s.levenshtein_distance,
        s.levenshtein_score,
        s.token_score,
        s.trigram_score,
        s.final_score,
        s.is_exact_normalised_match,
        s.is_phonetic_match
    FROM company AS c
    CROSS JOIN LATERAL fn_score_normalised_names(search_name, c.normalised_name) AS s
    WHERE c.company_id = p_company_id;
END;
$$;

-- Ready for a larger table. The candidate function still scans rows at this size.
CREATE INDEX IF NOT EXISTS ix_company_normalised_trgm
    ON company USING gin (normalised_name gin_trgm_ops);

CREATE TABLE IF NOT EXISTS match_case (
    case_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    search_name varchar(300) NOT NULL,
    expected_name varchar(300) NOT NULL,
    should_match boolean NOT NULL,
    minimum_score integer NOT NULL,
    maximum_score integer,
    rival_name varchar(300),
    note text NOT NULL
);

-- Runs the labelled cases. A positive case must land inside the score window
-- and, when a rival is named, beat that rival. A negative case must stay under the minimum.
CREATE OR REPLACE FUNCTION evaluate_match_cases()
RETURNS TABLE (
    case_id integer,
    search_name character varying,
    expected_name character varying,
    note text,
    actual_score integer,
    rival_score integer,
    passed boolean
)
LANGUAGE sql
STABLE
AS $$
    WITH scored AS (
        SELECT
            mc.case_id,
            mc.search_name,
            mc.expected_name,
            mc.should_match,
            mc.minimum_score,
            mc.maximum_score,
            mc.rival_name,
            mc.note,
            (
                SELECT m.final_score
                FROM find_company_matches(mc.search_name, 0, 200) AS m
                WHERE m.company_name = mc.expected_name
                LIMIT 1
            ) AS actual_score,
            (
                SELECT m.final_score
                FROM find_company_matches(mc.search_name, 0, 200) AS m
                WHERE m.company_name = mc.rival_name
                LIMIT 1
            ) AS rival_score
        FROM match_case AS mc
    )
    SELECT
        scored.case_id,
        scored.search_name,
        scored.expected_name,
        scored.note,
        scored.actual_score,
        scored.rival_score,
        CASE
            WHEN scored.should_match THEN
                scored.actual_score IS NOT NULL
                AND scored.actual_score >= scored.minimum_score
                AND (scored.maximum_score IS NULL OR scored.actual_score <= scored.maximum_score)
                AND (
                    scored.rival_name IS NULL
                    OR scored.actual_score > COALESCE(scored.rival_score, -1)
                )
            ELSE
                scored.actual_score IS NULL OR scored.actual_score < scored.minimum_score
        END AS passed
    FROM scored
    ORDER BY scored.case_id;
$$;

-- Scores two typed names. They do not have to exist in the company table.
CREATE OR REPLACE FUNCTION compare_company_names(p_left text, p_right text)
RETURNS TABLE (
    normalised_left text,
    normalised_right text,
    exact_match_score integer,
    soundex_score integer,
    difference_score integer,
    levenshtein_distance integer,
    levenshtein_score integer,
    token_score integer,
    trigram_score integer,
    final_score integer,
    is_exact_normalised_match boolean,
    is_phonetic_match boolean
)
LANGUAGE plpgsql
STABLE
AS $$
#variable_conflict use_column
DECLARE
    left_name text;
    right_name text;
BEGIN
    left_name := fn_normalise_company_name(p_left);
    right_name := fn_normalise_company_name(p_right);

    IF left_name IS NULL OR right_name IS NULL THEN
        RETURN;
    END IF;

    RETURN QUERY
    SELECT
        left_name,
        right_name,
        s.exact_match_score,
        s.soundex_score,
        s.difference_score,
        s.levenshtein_distance,
        s.levenshtein_score,
        s.token_score,
        s.trigram_score,
        s.final_score,
        s.is_exact_normalised_match,
        s.is_phonetic_match
    FROM fn_score_normalised_names(left_name, right_name) AS s;
END;
$$;
