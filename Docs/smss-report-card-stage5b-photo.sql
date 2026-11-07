-- Examination / Report Card, STAGE 5b, part 2: the optional student photo on a report card.
--
-- Adds show_photo to tb_school_report_card_settings (created in the stage 5a migration). Additive and idempotent (safe to run twice); no existing row is changed.
--  * show_photo boolean NOT NULL DEFAULT false: OFF unless the school switches it on, so every school keeps printing cards exactly as before.
--  * Only the designs with a photo slot (skill-grid, playful-report, board-style) draw it; the other designs ignore the setting.
--  * The photo itself is never stored here or in a published card: a card remembers only the student's photo KEY and the file is read at the moment the card is drawn.

BEGIN;

ALTER TABLE public.tb_school_report_card_settings ADD COLUMN IF NOT EXISTS show_photo boolean NOT NULL DEFAULT false;

COMMIT;
