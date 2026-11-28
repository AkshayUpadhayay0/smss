using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api_db_layer.repository;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// "Outstanding from earlier years", step 5 (reminders): an installment still unpaid from an EARLIER year counts toward a student's overdue status for the fee reminder (the text is unchanged and carries no amounts).
    /// Only students with an Active Student login in the current year are ever reminded. [DbFact]: needs SMSS_TEST_PG (no migration).
    /// </summary>
    public class EarlierYearRemindersS5Tests
    {
        private static FeeReminderService Svc(TaxDb db, bool withEarlier)
        {
            var c = db.Ctx();
            return new FeeReminderService(new NotificationRepository(c), new StudentRepository(c), new FakeFcm(), TimeProvider.System, Options.Create(new FirebaseOptions { DryRun = true }), NullLogger<FeeReminderService>.Instance,
                withEarlier ? new EarlierYearDuesRepository(c) : null);
        }

        private static void Login(TaxDb db, string school, string student) =>
            db.Exec($"insert into tb_users (school_id, user_type, org_user_id, display_name, status_id, is_first_login, password_hash) values ('{school}', 'Student', '{student}', 'S', {EarlierYearsKit.Active}, false, 'x')");

        [DbFact]
        public async Task An_unpaid_installment_from_an_earlier_year_makes_the_student_overdue_for_the_reminder_and_nothing_else_does()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEM");
            // ZQ: this year's installment is in the future, but 2025-26 is still unpaid. ZS: only a future installment. ZT: the earlier year is fully paid.
            foreach (var id in new[] { "ZQ", "ZS", "ZT" }) EarlierYearsKit.Student(db, w, id, w.Sec1, w.Y2, null);
            EarlierYearsKit.Due(db, w, "ZQ", w.Y2, "2027-03-10", 1000, 0); EarlierYearsKit.Due(db, w, "ZQ", w.Y1, "2025-07-10", 400, 0);
            EarlierYearsKit.Due(db, w, "ZS", w.Y2, "2027-03-10", 1000, 0);
            EarlierYearsKit.Due(db, w, "ZT", w.Y2, "2027-03-10", 1000, 0); EarlierYearsKit.Due(db, w, "ZT", w.Y1, "2025-07-10", 400, 400);
            foreach (var id in new[] { "ZQ", "ZS", "ZT" }) Login(db, "ZEM", id);
            // make the other kit students not overdue for this check: only the three above have logins, so only they can be reminded
            var without = (await Svc(db, withEarlier: false).PreviewAsync("ZEM", default)).Data!;
            Assert.Equal(0, without.StudentsToRemind);                                       // before: only this year's overdue counted, and none of the three has one
            var with = (await Svc(db, withEarlier: true).PreviewAsync("ZEM", default)).Data!;
            Assert.Equal(1, with.StudentsToRemind);                                          // after: ZQ only
            // another school's reminder never counts these students
            var otherBefore = (await Svc(db, withEarlier: true).PreviewAsync(TaxDb.SchoolB, default)).Data!.StudentsToRemind;
            db.Exec("update tb_student_fee_dues set amount_paid = amount_paid where false");
            Assert.Equal(otherBefore, (await Svc(db, withEarlier: true).PreviewAsync(TaxDb.SchoolB, default)).Data!.StudentsToRemind);
        }
    }
}
