using smss_api_db_layer.constants;
using smss_api_db_layer.entity;

namespace smss_api_service_layer.helper
{
    /// <summary>Finds the default admission type of a path (New Admission / Existing Student) by CODE and says clearly when it is missing or inactive.</summary>
    public static class AdmissionTypeDefaults
    {
        public static async Task<(LutAdmissionType? Type, string? Error)> ResolveAsync(Func<string, CancellationToken, Task<LutAdmissionType?>> byCode, string code, CancellationToken ct)
        {
            var label = AdmissionTypeCodes.LabelOf(code);
            var type = await byCode(code, ct);
            if (type == null) return (null, $"The '{label}' Admission Type is not set up. Please contact support.");
            if (!type.IsActive) return (null, $"The '{label}' Admission Type is inactive. Please contact support.");
            return (type, null);
        }
    }
}
