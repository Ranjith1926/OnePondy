-- Creates the local development role and database (spec §13, §17.2). Safe to run more than once.
-- Run as a PostgreSQL administrator, choosing your own password for the app role:
--
--   psql -U postgres -v app_password=<choose-a-password> -f database/scripts/create-dev-database.sql
--
-- PostGIS is enabled separately with enable-postgis.sql once the PostGIS package is installed.

\set ON_ERROR_STOP on

\if :{?app_password}
\else
  \echo 'Missing password. Re-run with: -v app_password=<choose-a-password>'
  \quit
\endif

SELECT format('CREATE ROLE onepondy_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'onepondy_app') \gexec

SELECT 'CREATE DATABASE onepondy_dev OWNER onepondy_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'onepondy_dev') \gexec

\echo 'Ready: database onepondy_dev owned by role onepondy_app.'
