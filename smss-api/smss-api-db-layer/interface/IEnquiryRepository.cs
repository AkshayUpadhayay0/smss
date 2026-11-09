using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>A list row: only what the table shows (no message body, no user agent).</summary>
    public record EnquiryListRow(long EnquiryId, string EnquiryType, string FullName, string? SchoolName, string Email, string? Phone, string? City, string? Subject, string Status, DateTime CreatedAt);

    /// <summary>Dates filter on the day submitted (FromUtc inclusive, ToExclusiveUtc exclusive; already UTC instants).</summary>
    public record EnquiryFilter(string? Type, string? Status, DateTime? FromUtc, DateTime? ToExclusiveUtc, string? Search, bool OldestFirst);

    public record EnquiryDetailRow(TbEnquiry Enquiry, string? HandledByName);

    public record EnquiryCounts(int New, int Contacted, int DemoScheduled, int NewDemo, int NewContact, int TotalThisMonth);

    public interface IEnquiryRepository
    {
        Task AddAsync(TbEnquiry enquiry, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<(List<EnquiryListRow> Rows, int Total)> ListAsync(EnquiryFilter filter, int page, int pageSize, CancellationToken ct);
        Task<EnquiryDetailRow?> GetDetailAsync(long id, CancellationToken ct);
        Task<TbEnquiry?> GetForUpdateAsync(long id, CancellationToken ct);
        Task<EnquiryCounts> GetCountsAsync(DateTime monthStartUtc, CancellationToken ct);
    }
}
