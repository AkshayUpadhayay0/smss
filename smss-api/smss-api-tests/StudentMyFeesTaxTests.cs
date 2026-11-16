using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>A student's own payment list carries fee, tax and total per payment; a payment without tax keeps today's shape; voided ones stay hidden.</summary>
    public class StudentMyFeesTaxTests
    {
        private static async Task<StudentMyFeesDto> Run(List<PaymentSummaryDto> payments)
        {
            var students = Fake.Of<IStudentRepository>(new()
            {
                ["GetUserLoginIdAsync"] = "DPS001ST001",
                ["GetStudentAsync"] = new TbStudent { StudentId = "DPS001ST001" },
            });
            var billing = Fake.Of<IStudentFeeBillingService>(new()
            {
                ["GetItemsAsync"] = Fake.Ok(new StudentFeeItemsDto { StudentId = "DPS001ST001" }),
                ["GetDuesAsync"] = Fake.Ok(new StudentFeeDuesDto { StudentId = "DPS001ST001" }),
            });
            var pays = Fake.Of<IFeePaymentService>(new() { ["GetPaymentsAsync"] = Fake.Ok(payments) });
            var res = await new StudentMyFeesService(students, billing, pays, NullLogger<StudentMyFeesService>.Instance).GetMyFeesAsync("DPS261013001", 7, CancellationToken.None);
            Assert.True(res.Status, res.Message);
            return res.Data!;
        }

        [Fact]
        public async Task Payments_CarryFeeTaxAndTotal_AndATaxFreePaymentHasNoTaxName()
        {
            var dto = await Run(new()
            {
                new PaymentSummaryDto { PaymentId = 2, ReceiptNumber = "RCP-2026-0002", PaymentDate = new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc), PaymentMode = "Cash", Status = "Active",
                    Amount = 1050m, FeeAmount = 1000m, TaxName = "GST", TaxCode = "GST", TaxRatePercent = 5m, TaxAmount = 50m, TotalAmount = 1050m },
                new PaymentSummaryDto { PaymentId = 1, ReceiptNumber = "RCP-2026-0001", PaymentDate = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc), PaymentMode = "UPI", Status = "Active",
                    Amount = 3250m, FeeAmount = 3250m, TaxAmount = 0m, TotalAmount = 3250m },
                new PaymentSummaryDto { PaymentId = 3, ReceiptNumber = "RCP-2026-0003", PaymentDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), PaymentMode = "Cash", Status = "Voided",
                    Amount = 105m, FeeAmount = 100m, TaxName = "GST", TaxRatePercent = 5m, TaxAmount = 5m, TotalAmount = 105m },
            });

            Assert.Equal(new[] { "RCP-2026-0002", "RCP-2026-0001" }, dto.Payments.Select(p => p.ReceiptNumber));   // voided one hidden, newest first
            var taxed = dto.Payments[0];
            Assert.Equal((1000m, "GST", 5m, 50m, 1050m, 1050m), (taxed.FeeAmount, taxed.TaxName, taxed.TaxRatePercent, taxed.TaxAmount, taxed.TotalAmount, taxed.Amount));
            var plain = dto.Payments[1];
            Assert.Null(plain.TaxName);
            Assert.Equal((3250m, 0m, 3250m, 3250m), (plain.FeeAmount, plain.TaxAmount, plain.TotalAmount, plain.Amount));
        }
    }
}
