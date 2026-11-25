using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record SchoolSenderInfo(string SchoolId, string SchoolName, string? WhatsAppNumber);

    // Every method takes the school id explicitly; the service decides which school the caller may reach.
    public interface ISchoolWhatsAppSenderRepository
    {
        /// <summary>The school's name and its own WhatsApp number (tb_schools), or null when there is no such school.</summary>
        Task<SchoolSenderInfo?> GetSchoolAsync(string schoolId, CancellationToken ct);
        Task<TbSchoolWhatsAppSender?> GetSenderAsync(string schoolId, bool tracked, CancellationToken ct);
        Task AddSenderAsync(TbSchoolWhatsAppSender sender, CancellationToken ct);
        Task<string?> GetUserNameAsync(long userId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();
    }
}
