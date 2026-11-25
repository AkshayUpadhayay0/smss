using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>An existing guardian picked for a new student, with the relationship this student will have to them (null = the guardian's own).</summary>
    public sealed record GuardianPick(long GuardianId, string? Relationship);

    public sealed class GuardianReuseResult
    {
        /// <summary>The existing guardian who becomes the student's PRIMARY guardian; null when the primary one is a person typed in (a new row).</summary>
        public TbGuardian? Primary { get; set; }
        /// <summary>The other existing guardians to link (not primary), each with the relationship for this student.</summary>
        public List<(TbGuardian Guardian, string? Relationship)> Extras { get; } = new();
        public (int Code, string Message)? Error { get; set; }
    }

    /// <summary>
    /// A sibling's parents are LINKED to a new student, never copied: the picked guardians are resolved to their survivor when they were merged (merged_into_guardian_id), an inactive guardian is refused
    /// with a clear message, and when asked the rows are locked FOR UPDATE (in id order) in the caller's transaction so two confirmations that share a guardian queue up.
    /// </summary>
    public static class GuardianReuse
    {
        public static async Task<GuardianReuseResult> LoadAsync(IAdmissionRepository repo, string schoolId, long? primaryId, IReadOnlyList<GuardianPick> extras, int activeStatusId, bool lockRows, string action, CancellationToken ct)
        {
            var result = new GuardianReuseResult();
            var wanted = new List<long>();
            if (primaryId != null) wanted.Add(primaryId.Value);
            wanted.AddRange(extras.Select(e => e.GuardianId));
            if (wanted.Count == 0) return result;

            // follow merges: a guardian merged into another is replaced by the survivor; one that is not this school's is a 404 (ids from another school are never confirmed)
            var survivors = await repo.ResolveGuardianSurvivorsAsync(schoolId, wanted, ct);
            if (wanted.Any(id => survivors.GetValueOrDefault(id) == null))
            {
                result.Error = (404, "A selected guardian was not found for your school.");
                return result;
            }
            long? primarySurvivor = primaryId == null ? null : survivors[primaryId.Value];
            var extraPicks = new List<(long Id, string? Relationship)>();
            foreach (var e in extras)
            {
                var id = survivors[e.GuardianId]!.Value;
                if (id == primarySurvivor || extraPicks.Any(x => x.Id == id)) continue;   // the same person picked twice (or merged into one) is linked once
                extraPicks.Add((id, e.Relationship));
            }

            var ids = new List<long>(extraPicks.Select(x => x.Id));
            if (primarySurvivor != null) ids.Add(primarySurvivor.Value);
            var rows = lockRows ? await repo.LockGuardiansAsync(schoolId, ids, ct) : await repo.GetGuardiansReadOnlyAsync(schoolId, ids, ct);
            if (rows.Count != ids.Distinct().Count())
            {
                result.Error = (404, "A selected guardian was not found for your school.");
                return result;
            }
            var inactive = rows.FirstOrDefault(g => g.StatusId != activeStatusId || g.MergedIntoGuardianId != null);
            if (inactive != null)
            {
                result.Error = (409, $"The guardian {Display(inactive)} is inactive, so the application cannot be {action}. Edit it and choose another guardian, or enter the parent as a new person.");
                return result;
            }
            if (primarySurvivor != null) result.Primary = rows.First(g => g.GuardianId == primarySurvivor.Value);
            foreach (var (id, rel) in extraPicks) result.Extras.Add((rows.First(g => g.GuardianId == id), rel));
            return result;
        }

        public static string Display(TbGuardian g) => string.Join(' ', new[] { g.FirstName, g.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        /// <summary>
        /// The WhatsApp number and email the admin entered for an existing guardian are written to the SHARED guardian row, but only where the row has none: a stored value is never overwritten here
        /// (the guardian's details are changed on the edit screen). Every change is logged (source EDIT). Returns the log rows to save in the same transaction.
        /// </summary>
        public static List<TbGuardianChange> ApplyContact(TbGuardian guardian, string? whatsApp, string? email, long userId, string schoolId, DateTime now)
        {
            var changes = new List<TbGuardianChange>();
            void Log(string field, string? newValue) => changes.Add(new TbGuardianChange { SchoolId = schoolId, GuardianId = guardian.GuardianId, FieldName = field, OldValue = null, NewValue = newValue, Source = "EDIT", ChangedBy = userId, ChangedAt = now });
            if (string.IsNullOrWhiteSpace(guardian.WhatsAppNumber) && !string.IsNullOrWhiteSpace(whatsApp)) { guardian.WhatsAppNumber = whatsApp.Trim(); Log("whatsapp_number", guardian.WhatsAppNumber); }
            if (string.IsNullOrWhiteSpace(guardian.Email) && !string.IsNullOrWhiteSpace(email)) { guardian.Email = email.Trim(); Log("email", guardian.Email); }
            if (changes.Count > 0) guardian.UpdatedAt = now;
            return changes;
        }
    }
}
