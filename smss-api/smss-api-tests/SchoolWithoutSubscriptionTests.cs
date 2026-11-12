using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// A school registered before billing existed (e.g. the Super Admin's own school) has NO tb_school_subscriptions row, and
    /// since the legacy tb_schools.subscription_* columns are gone nothing else can describe its subscription. These tests pin
    /// what the Subscriptions screen, the dashboard list and invoice generation do for such a school.
    /// </summary>
    public class SchoolWithoutSubscriptionTests
    {
        /// <summary>Repository stand-in: every method answers with the default value (null / 0 / empty), except those in <paramref name="answers"/>.</summary>
        private class FakeRepo : DispatchProxy
        {
            public Dictionary<string, object?> Answers { get; } = new();

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                var name = method!.Name;
                var ret = method.ReturnType;
                if (!ret.IsGenericType || ret.GetGenericTypeDefinition() != typeof(Task<>)) throw new NotSupportedException(name);
                var inner = ret.GetGenericArguments()[0];
                var value = Answers.TryGetValue(name, out var a) ? a : (inner.IsValueType ? Activator.CreateInstance(inner) : null);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { value });
            }
        }

        private static (ISubscriptionRepository Repo, FakeRepo Fake) NewRepo()
        {
            var repo = DispatchProxy.Create<ISubscriptionRepository, FakeRepo>();
            return (repo, (FakeRepo)(object)repo);
        }

        private const string NoRowMessage = "This school has no subscription record. It was registered before billing was added.";

        [Fact]
        public async Task SubscriptionsScreen_detail_is_a_clean_404_not_a_crash()
        {
            var (repo, _) = NewRepo();
            var svc = new SubscriptionBillingService(repo, NullLogger<SubscriptionBillingService>.Instance);

            var r = await svc.GetSubscriptionAsync("SUAD0000001", default);

            Assert.Equal(404, r.StatusCode);
            Assert.Equal(NoRowMessage, r.Message);
        }

        [Fact]
        public async Task Invoice_generation_for_a_school_without_a_row_is_refused_with_404()
        {
            var (repo, _) = NewRepo();
            var svc = new SubscriptionBillingService(repo, NullLogger<SubscriptionBillingService>.Instance);

            var r = await svc.GenerateInvoiceAsync("SUAD0000001", 1, new GenerateInvoiceRequest(), default);

            Assert.Equal(404, r.StatusCode);
            Assert.Equal(NoRowMessage, r.Message);
            Assert.Null(r.Data);
        }

        [Fact]
        public async Task Configuring_billing_or_recording_a_payment_for_a_school_without_a_row_is_404()
        {
            var (repo, fake) = NewRepo();
            fake.Answers["SchoolExistsAsync"] = true;
            var svc = new SubscriptionBillingService(repo, NullLogger<SubscriptionBillingService>.Instance);

            var configure = await svc.ConfigureAsync("SUAD0000001", new ConfigureBillingRequest { BillingFrequency = "MONTHLY" }, default);
            var pay = await svc.RecordPaymentAsync(1, new RecordSubscriptionPaymentRequest { SchoolId = "SUAD0000001", Amount = 100m, PaymentMode = PaymentModes.All[0] }, default);

            Assert.Equal(404, configure.StatusCode);
            Assert.Equal(404, pay.StatusCode);
        }

        [Fact]
        public async Task Dashboard_school_list_shows_such_a_school_as_NOT_SET()
        {
            var (repo, fake) = NewRepo();
            fake.Answers["GetStatusIdByNameAsync"] = (int?)1;
            var row = new SchoolSubscriptionRow("SUAD0000001", "SUAD", "SMSS Admin", 1, null, false);
            fake.Answers["ListSchoolSubscriptionsAsync"] = (new List<SchoolSubscriptionRow> { row }, 1);
            fake.Answers["GetHeadcountsAsync"] = new Dictionary<string, SchoolHeadcount>();
            var svc = new SuperAdminDashboardService(repo, null!, NullLogger<SuperAdminDashboardService>.Instance);

            var r = await svc.ListSchoolsAsync(null, null, 1, 25, default);

            Assert.Equal(200, r.StatusCode);
            var item = Assert.Single(r.Data!.Items);
            Assert.Equal(SubscriptionStatuses.NotSet, item.SubscriptionStatus);
            Assert.Null(item.BillingFrequency);
            Assert.Null(item.TrialEndDate);
            Assert.False(item.TrialExpired);
            Assert.Equal(0, item.BillableUserCount);
        }

        [Fact]
        public void No_stored_status_is_NOT_SET_whatever_the_invoices_say()
        {
            Assert.Equal(SubscriptionStatuses.NotSet, SubscriptionRules.EffectiveStatus(null, false));
            Assert.Equal(SubscriptionStatuses.NotSet, SubscriptionRules.EffectiveStatus(null, true));
        }
    }
}
