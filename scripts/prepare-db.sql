-- Local PostgreSQL 16 setup. Dev passwords only. Do not use these outside a laptop.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_migrator') THEN
    CREATE ROLE cmr_migrator NOLOGIN;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_owner') THEN
    CREATE ROLE cmr_owner LOGIN PASSWORD 'cmr_owner_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_app') THEN
    CREATE ROLE cmr_app LOGIN PASSWORD 'cmr_app_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB;
  END IF;
END $$;
GRANT cmr_migrator TO cmr_owner WITH ADMIN OPTION;
SELECT 'CREATE DATABASE cmr OWNER cmr_owner' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'cmr')\gexec
GRANT CONNECT ON DATABASE cmr TO cmr_owner, cmr_app;
