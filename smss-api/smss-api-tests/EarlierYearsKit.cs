namespace smss_api_tests
{
    /// <summary>
    /// A scratch school for the "Outstanding from earlier years" tests: years 2025-26 (past) and 2026-27 (current), Classes 1 to 3 (section A, offered both years), one Tuition fee type.
    /// Students (dues are inserted directly; nothing is carried forward by any code):
    ///   ZP promoted WITH OVERRIDE: Class 1 in 2025-26 (outcome PROMOTED), Class 3 in 2026-27 (skipped Class 2). 2025-26: 300 unpaid + 100 paid. 2026-27: 1000 unpaid.
    ///   ZR REPEATED: Class 1 in both years. 2025-26: 500 unpaid (one due 2025-07-10). 2026-27: 1000 unpaid.
    ///   ZL LEFT: Class 1 in 2026-27 with outcome LEFT. 2025-26: 200 unpaid. 2026-27: 400 unpaid.
    ///   ZN never owed anything earlier: Class 1 in 2026-27 only, 1000 unpaid.
    /// </summary>
    internal static class EarlierYearsKit
    {
        public const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        public record World(string School, long Y1, long Y2, long C1, long C3, long Sec1, long Sec3, long Tuition, string T)
        {
            public string P => "ZP" + T; public string R => "ZR" + T; public string L => "ZL" + T; public string N => "ZN" + T;
        }

        public static World Build(TaxDb db, string s, string tag = "")
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_short_code, school_status_id) values ('{s}', '{s}-C', 'Earlier years {s}', 'E{s[^3..]}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{s}', '2025-26', '2025-04-01', '2026-03-31', false, {Active}), ('{s}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{s}', 'Class 1', 1, {Active}), ('{s}', 'Class 2', 2, {Active}), ('{s}', 'Class 3', 3, {Active})");
            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select '{s}', class_id, 'A', {Active} from tb_classes where school_id='{s}'");
            db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) values ('{s}', 'Tuition', 'TUI', false, true)");
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{s}', y.academic_year_id, c.class_id, {Active} from tb_academic_years y, tb_classes c where y.school_id='{s}' and c.school_id='{s}'");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{s}', y.academic_year_id, sec.class_id, sec.section_id, {Active} from tb_academic_years y, tb_sections sec where y.school_id='{s}' and sec.school_id='{s}'");
            long Y(string n) => db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{s}' and year_name='{n}'");
            long Cl(string n) => db.Scalar<long>($"select class_id from tb_classes where school_id='{s}' and class_name='{n}'");
            long Sec(string n) => db.Scalar<long>($"select section_id from tb_sections where school_id='{s}' and class_id={Cl(n)}");
            var w = new World(s, Y("2025-26"), Y("2026-27"), Cl("Class 1"), Cl("Class 3"), Sec("Class 1"), Sec("Class 3"), db.Scalar<long>($"select fee_type_id from tb_fee_types where school_id='{s}'"), tag);
            Student(db, w, w.P, w.Sec1, w.Y1, "PROMOTED"); Enrol(db, w, w.P, w.Sec3, w.Y2, null);
            Student(db, w, w.R, w.Sec1, w.Y1, "REPEATED"); Enrol(db, w, w.R, w.Sec1, w.Y2, null);
            Student(db, w, w.L, w.Sec1, w.Y1, "PROMOTED"); Enrol(db, w, w.L, w.Sec1, w.Y2, "LEFT");
            Student(db, w, w.N, w.Sec1, w.Y2, null);
            Due(db, w, w.P, w.Y1, "2025-06-10", 300, 0); Due(db, w, w.P, w.Y1, "2025-05-10", 100, 100); Due(db, w, w.P, w.Y2, "2026-05-10", 1000, 0);
            Due(db, w, w.R, w.Y1, "2025-07-10", 500, 0); Due(db, w, w.R, w.Y2, "2026-05-10", 1000, 0);
            Due(db, w, w.L, w.Y1, "2025-08-10", 200, 0); Due(db, w, w.L, w.Y2, "2026-06-10", 400, 0);
            Due(db, w, w.N, w.Y2, "2026-05-10", 1000, 0);
            return w;
        }

        private static void Student(TaxDb db, World w, string id, long section, long year, string? outcome)
        {
            db.Exec($@"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id)
                       values ('{id}', '{w.School}', '{id}-N', 'Stu', '{id}', '2015-01-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), '2025-04-01', {Active})");
            Enrol(db, w, id, section, year, outcome);
        }

        private static void Enrol(TaxDb db, World w, string id, long section, long year, string? outcome)
        {
            var leaving = outcome == "LEFT" ? ", leaving_date, leaving_reason, outcome_by" : "";
            var leavingVals = outcome == "LEFT" ? $", date '2026-09-01', 'Moved away', {TaxDb.UserA}" : "";
            db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id{(outcome != null ? ", outcome, outcome_at" : "")}{leaving})
                       values ('{w.School}', '{id}', {year}, {section}, '2025-04-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), {Active}{(outcome != null ? $", '{outcome}', now()" : "")}{leavingVals})");
        }

        public static void Due(TaxDb db, World w, string student, long year, string dueDate, decimal due, decimal paid)
        {
            var monthly = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='MONTHLY'");
            var item = db.Scalar<long?>($"select student_fee_item_id from tb_student_fee_items where school_id='{w.School}' and student_id='{student}' and academic_year_id={year} and fee_type_id={w.Tuition}");
            item ??= db.Scalar<long>($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount, is_applicable)
                                        values ('{student}', '{w.School}', {year}, {w.Tuition}, {monthly}, 1000, 1000, true) returning student_fee_item_id");
            db.Exec($"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid) values ({item}, '{student}', '{w.School}', '{dueDate}', {due}, {paid})");
        }
    }
}
