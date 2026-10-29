using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>The global rate card (what SMSS charges schools). Super Admin only; the controller checks the role.</summary>
    public interface ISubscriptionPricingService
    {
        Task<ApiResponse<SubscriptionPricingViewDto>> GetAsync(CancellationToken ct);
        Task<ApiResponse<SubscriptionPricingDto>> UpdateAsync(UpdateSubscriptionPricingRequest req, CancellationToken ct);
    }

    /// <summary>Configuring a school's billing, invoices and payments. Super Admin only; the controllers check the role.</summary>
    public interface ISubscriptionBillingService
    {
        Task<ApiResponse<SchoolSubscriptionDto>> GetSubscriptionAsync(string schoolId, CancellationToken ct);
        Task<ApiResponse<SchoolSubscriptionDto>> ConfigureAsync(string schoolId, ConfigureBillingRequest req, CancellationToken ct);
        /// <summary>Replaces the school's negotiated terms (null rate / discount = global). Allowed any time before cancellation; affects invoices generated afterwards only.</summary>
        Task<ApiResponse<SchoolSubscriptionDto>> UpdateTermsAsync(string schoolId, BillingTermsRequest req, CancellationToken ct);

        Task<ApiResponse<SubscriptionInvoiceDto>> GenerateInvoiceAsync(string schoolId, long generatedByUserId, GenerateInvoiceRequest req, CancellationToken ct);
        Task<ApiResponse<SubscriptionInvoicePagedDto>> ListInvoicesAsync(string? schoolId, string? status, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<SubscriptionInvoiceDto>> GetInvoiceAsync(long invoiceId, CancellationToken ct);

        Task<ApiResponse<SubscriptionPaymentDto>> RecordPaymentAsync(long recordedByUserId, RecordSubscriptionPaymentRequest req, CancellationToken ct);
        Task<ApiResponse<SubscriptionPaymentDto>> VoidPaymentAsync(long paymentId, long voidedByUserId, VoidSubscriptionPaymentRequest req, CancellationToken ct);
    }

    /// <summary>The read side of the Super Admin dashboard (sections A and B).</summary>
    public interface ISuperAdminDashboardService
    {
        Task<ApiResponse<SuperAdminSummaryDto>> GetSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<SchoolSubscriptionPagedDto>> ListSchoolsAsync(string? search, string? status, int page, int pageSize, CancellationToken ct);
    }
}
