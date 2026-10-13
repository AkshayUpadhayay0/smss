-- Login ID rework: tb_users.org_user_id is the login credential, tb_users.username becomes a display label.
-- Also adds the short code used to build student/employee login ids (e.g. DPS001ST001).
--
-- Run this BEFORE starting the API build that contains the TbUsers.DisplayName / TbSchools.SchoolShortCode mappings: once those are deployed every query on tb_users and tb_schools references the new columns.
-- Safe to run twice.

-- 1. username -> display_name (not unique any more: two students / schools can share a name)
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'tb_users' AND column_name = 'username')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'tb_users' AND column_name = 'display_name') THEN
        ALTER TABLE public.tb_users RENAME COLUMN username TO display_name;
    END IF;
END $$;

DROP INDEX IF EXISTS public.uq_tb_users_username;                      -- the unique index on the old username
ALTER INDEX IF EXISTS public.idx_tb_users_username RENAME TO idx_tb_users_display_name;   -- plain index, kept for search

-- 2. Short code for building student / employee login ids. Separate from the date-based school_id.
--    Existing schools keep NULL; creating a student login for such a school is refused with a clear message
--    (see the optional backfill below).
ALTER TABLE public.tb_schools
    ADD COLUMN IF NOT EXISTS school_short_code varchar(10);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_schools_short_code
    ON public.tb_schools (school_short_code);

-- (A tb_users.student_id link used to be added here. It is not needed: a student's login id IS the student id, see
--  smss-student-id-varchar.sql, which also removes the column if an earlier version of this file added it.)

-- OPTIONAL, run by hand if you want student logins for schools registered before this change. Pick the code yourself;
-- it must be unique and is what appears in every student's login id (e.g. FTS001 -> FTS001ST001).
--   UPDATE public.tb_schools SET school_short_code = 'FTS001' WHERE school_id = 'sch2026006';
