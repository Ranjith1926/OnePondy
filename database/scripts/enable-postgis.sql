-- Enables PostGIS in the development database. Needed from Migration 1 (Phases 2–4).
-- Creating an extension usually needs administrator rights that the app role should not have,
-- so a DBA runs this script before migrations (spec §18.5):
--
--   psql -U postgres -f database/scripts/enable-postgis.sql
--
-- Install PostGIS first: PostgreSQL Stack Builder > Spatial Extensions > PostGIS.

\set ON_ERROR_STOP on
\connect onepondy_dev
CREATE EXTENSION IF NOT EXISTS postgis;
SELECT postgis_full_version();
