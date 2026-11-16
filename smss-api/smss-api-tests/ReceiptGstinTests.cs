using Xunit;

namespace smss_api_tests
{
    /// <summary>The school's GSTIN prints on a receipt that carries tax, when the school has one. Never on a receipt without tax, never when empty. Scratch PostgreSQL only (see TaxTestKit).</summary>
    public class ReceiptGstinTests
    {
        private const string Gstin = "09ABCDE1234F1Z5";

        [DbFact]
        public async Task TaxReceipt_CarriesTheSchoolsGstin_ATaxFreeReceiptNever()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_schools set school_gstin='{Gstin}' where school_id='{TaxDb.SchoolA}'");
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);

            var taxed = await db.Pay("GHS001ST001", 1000, gst.TaxTypeId);
            Assert.Equal(Gstin, taxed.Data!.School.Gstin);                                   // the response to the payment itself
            var again = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, taxed.Data.PaymentId, default)).Data!;
            Assert.Equal(Gstin, again.School.Gstin);                                         // a reprint

            var plain = await db.Pay("GHS001ST002", 500);
            Assert.Null(plain.Data!.School.Gstin);                                           // no tax, no GSTIN, even though the school has one
            Assert.Null((await db.Payments().GetReceiptAsync(TaxDb.SchoolA, plain.Data.PaymentId, default)).Data!.School.Gstin);

            var v = await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, taxed.Data.PaymentId, TaxDb.UserA, new smss_api_service_layer.dto.VoidPaymentRequest { VoidReason = "wrong student" }, default);
            Assert.Equal(200, v.StatusCode);
            Assert.Equal(Gstin, (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, taxed.Data.PaymentId, default)).Data!.School.Gstin);   // a voided tax receipt still prints it
        }

        [DbFact]
        public async Task NoGstinOnFile_OrOnlyBlanks_MeansNothingIsSent_AndOneSchoolsGstinNeverLeaksToAnother()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            db.Exec($"update tb_schools set school_gstin=null where school_id='{TaxDb.SchoolA}'");
            Assert.Null((await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!.School.Gstin);

            db.Exec($"update tb_schools set school_gstin='   ' where school_id='{TaxDb.SchoolA}'");
            Assert.Null((await db.Pay("GHS001ST002", 1000, gst.TaxTypeId)).Data!.School.Gstin);

            db.Exec($"update tb_schools set school_gstin='{Gstin}' where school_id='{TaxDb.SchoolB}'");
            Assert.Null((await db.Pay("GHS001ST003", 1000, gst.TaxTypeId)).Data!.School.Gstin);   // school A's own (empty) GSTIN, not school B's
        }
    }
}
