SELECT * FROM company


CALL usp_add_company('Harbour Lights Cafe Ltd', NULL);



SELECT company_id, company_name, normalised_name
FROM company
WHERE company_id = 149;
