-- Creates the database user for the API's managed identity (id-expenses-api) and grants it
-- read/write on the application data. Run by the deploy workflow as a SQL admin (the Expenses
-- SQL Admins group) before migrations. Idempotent: safe to run on every deploy.
--
-- The user is created WITH SID (derived from the identity's client ID, passed in as the sqlcmd
-- variable ApiIdentitySid) rather than FROM EXTERNAL PROVIDER, so the SQL server does not need
-- Directory Readers permission in Entra ID, and a `recreate` that regenerates the identity keeps
-- working because the deploy recomputes the SID each time. TYPE = E marks an Entra ID principal.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-expenses-api')
    CREATE USER [id-expenses-api] WITH SID = $(ApiIdentitySid), TYPE = E;

IF IS_ROLEMEMBER('db_datareader', 'id-expenses-api') = 0
    ALTER ROLE db_datareader ADD MEMBER [id-expenses-api];

IF IS_ROLEMEMBER('db_datawriter', 'id-expenses-api') = 0
    ALTER ROLE db_datawriter ADD MEMBER [id-expenses-api];
