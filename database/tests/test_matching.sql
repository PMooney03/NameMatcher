-- Run after the schema, functions and seed are loaded:
--   SELECT fn_levenshtein_distance('kitten', 'sitting');
-- This script raises if a check fails.

DO $$
BEGIN
    IF fn_levenshtein_distance('kitten', 'sitting') <> 3 THEN
        RAISE EXCEPTION 'kitten/sitting distance expected 3, got %', fn_levenshtein_distance('kitten', 'sitting');
    END IF;

    IF fn_levenshtein_distance('STEPHEN', 'STEVEN') <> 2 THEN
        RAISE EXCEPTION 'STEPHEN/STEVEN distance expected 2';
    END IF;

    IF fn_levenshtein_distance('STEPHEN', 'STEFEN') <> 2 THEN
        RAISE EXCEPTION 'STEPHEN/STEFEN distance expected 2';
    END IF;

    IF fn_levenshtein_distance('same', 'same') <> 0 THEN
        RAISE EXCEPTION 'identical strings should have distance 0';
    END IF;

    IF fn_levenshtein_distance('', 'abc') <> 3 THEN
        RAISE EXCEPTION 'empty string distance should be the other length';
    END IF;

    IF fn_levenshtein_distance(NULL, 'abc') IS NOT NULL THEN
        RAISE EXCEPTION 'NULL distance should be NULL';
    END IF;

    IF fn_levenshtein_similarity('same', 'same') <> 100 THEN
        RAISE EXCEPTION 'identical similarity should be 100';
    END IF;

    IF fn_levenshtein_similarity(NULL, 'abc') <> 0 THEN
        RAISE EXCEPTION 'NULL similarity should be 0';
    END IF;

    IF fn_normalise_company_name('O''Reilly Technologies Limited') <> 'OREILLY TECHNOLOGIES' THEN
        RAISE EXCEPTION 'O''Reilly normalisation failed: %', fn_normalise_company_name('O''Reilly Technologies Limited');
    END IF;

    IF fn_normalise_company_name('ACME Software Ltd') <> 'ACME SOFTWARE' THEN
        RAISE EXCEPTION 'Ltd normalisation failed: %', fn_normalise_company_name('ACME Software Ltd');
    END IF;

    IF fn_normalise_company_name('Acme Software Limited') <> 'ACME SOFTWARE' THEN
        RAISE EXCEPTION 'Limited normalisation failed';
    END IF;

    IF fn_normalise_company_name('O Riley Technologies') <> 'ORILEY TECHNOLOGIES' THEN
        RAISE EXCEPTION 'O Riley normalisation failed: %', fn_normalise_company_name('O Riley Technologies');
    END IF;

    IF fn_phonetic_score('STEPHEN', 'STEVEN') <> 100 THEN
        RAISE EXCEPTION 'Stephen/Steven phonetic score expected 100, got %', fn_phonetic_score('STEPHEN', 'STEVEN');
    END IF;

    IF fn_token_similarity('STEPHEN ENGINEERING', 'STEVEN ENGINEERING') < 80 THEN
        RAISE EXCEPTION 'token similarity Stephen/Steven too low: %',
            fn_token_similarity('STEPHEN ENGINEERING', 'STEVEN ENGINEERING');
    END IF;

    IF fn_token_similarity('STEPHEN ENGINEERING', 'ZENITH LOGISTICS') >= 50 THEN
        RAISE EXCEPTION 'unrelated token similarity too high';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM fn_explain_token_pairs('STEPHEN ENGINEERING', 'STEVEN ENGINEERING') AS pair
        WHERE pair.search_word = 'ENGINEERING'
          AND pair.candidate_word = 'ENGINEERING'
          AND pair.word_score = 100
    ) THEN
        RAISE EXCEPTION 'ENGINEERING should pair with itself';
    END IF;

    IF (SELECT comparison.final_score FROM compare_company_names('ACME Software Ltd', 'Acme Software Limited') AS comparison) <> 100 THEN
        RAISE EXCEPTION 'compare_company_names should treat Ltd and Limited as exact';
    END IF;
END $$;

-- Behaviour against the seeded companies.
DO $$
DECLARE
    acme_score integer;
    stephen_score integer;
    stefen_score integer;
    oreilly_score integer;
    oriley_score integer;
    mac_score integer;
    mcdonald_score integer;
    zenith_score integer;
    apple_score integer;
BEGIN
    SELECT m.final_score INTO acme_score
    FROM find_company_matches('ACME Software Ltd', 0, 50) AS m
    WHERE m.company_name = 'Acme Software Limited';

    IF acme_score IS DISTINCT FROM 100 THEN
        RAISE EXCEPTION 'ACME Limited vs Ltd should be an exact normalised match, got %', acme_score;
    END IF;

    SELECT m.final_score INTO stephen_score
    FROM find_company_matches('Steven Engineering Ltd', 0, 50) AS m
    WHERE m.company_name = 'Stephen Engineering Limited';

    IF stephen_score IS NULL OR stephen_score < 80 THEN
        RAISE EXCEPTION 'Stephen vs Steven scored too low: %', stephen_score;
    END IF;

    SELECT m.final_score INTO stefen_score
    FROM find_company_matches('Stefen Engineering', 0, 50) AS m
    WHERE m.company_name = 'Stephen Engineering Limited';

    IF stefen_score IS NULL OR stefen_score < 80 THEN
        RAISE EXCEPTION 'Stephen vs Stefen scored too low: %', stefen_score;
    END IF;

    SELECT m.final_score INTO oreilly_score
    FROM find_company_matches('OReilly Technology Ltd', 0, 50) AS m
    WHERE m.company_name = 'O''Reilly Technologies Limited';

    IF oreilly_score IS NULL OR oreilly_score < 80 THEN
        RAISE EXCEPTION 'O''Reilly vs OReilly scored too low: %', oreilly_score;
    END IF;

    SELECT m.final_score INTO oriley_score
    FROM find_company_matches('O Riley Technologies', 0, 50) AS m
    WHERE m.company_name = 'O''Reilly Technologies Limited';

    IF oriley_score IS NULL OR oriley_score < 75 THEN
        RAISE EXCEPTION 'O''Reilly vs O Riley scored too low: %', oriley_score;
    END IF;

    SELECT m.final_score INTO mac_score
    FROM find_company_matches('McDonnell Construction', 0, 50) AS m
    WHERE m.company_name = 'MacDonnell Construction';

    IF mac_score IS NULL OR mac_score < 90 THEN
        RAISE EXCEPTION 'McDonnell vs MacDonnell scored too low: %', mac_score;
    END IF;

    SELECT m.final_score INTO mcdonald_score
    FROM find_company_matches('McDonnell Construction', 0, 50) AS m
    WHERE m.company_name = 'McDonald Construction';

    IF mcdonald_score IS NULL OR mcdonald_score >= mac_score THEN
        RAISE EXCEPTION 'McDonald should rank below MacDonnell. McDonald %, MacDonnell %', mcdonald_score, mac_score;
    END IF;

    SELECT m.final_score INTO zenith_score
    FROM find_company_matches('Stephen Engineering Limited', 0, 100) AS m
    WHERE m.company_name = 'Zenith Logistics Limited';

    IF zenith_score IS NOT NULL AND zenith_score >= 50 THEN
        RAISE EXCEPTION 'Unrelated Zenith scored too high: %', zenith_score;
    END IF;

    SELECT m.final_score INTO apple_score
    FROM find_company_matches('Appletree Technologies', 0, 50) AS m
    WHERE m.company_name = 'Apple Technologies';

    IF apple_score IS NULL OR apple_score >= 95 THEN
        RAISE EXCEPTION 'Apple vs Appletree should not look exact: %', apple_score;
    END IF;

    IF EXISTS (SELECT 1 FROM evaluate_match_cases() AS result WHERE NOT result.passed) THEN
        RAISE EXCEPTION 'one or more labelled match cases failed';
    END IF;
END $$;

DO $$
DECLARE
    new_id integer;
    blank_id integer;
    stored_name text;
BEGIN
    CALL usp_add_company('Procedure Pipeline Ltd', new_id);

    SELECT normalised_name INTO stored_name
    FROM fn_get_stored_company(new_id);

    IF stored_name IS DISTINCT FROM 'PROCEDURE PIPELINE' THEN
        RAISE EXCEPTION 'usp_add_company stored %, expected PROCEDURE PIPELINE', stored_name;
    END IF;

    CALL usp_delete_company(new_id);

    BEGIN
        CALL usp_add_company('   ', blank_id);
        RAISE EXCEPTION 'blank name should have been rejected';
    EXCEPTION
        WHEN invalid_parameter_value THEN
            NULL;
    END;
END $$;
