namespace smss_api_service_layer.dto
{
    /// <summary>Options of one fee sync of one student (SyncAsync). All optional: the default is the normal rule.</summary>
    public class FeeSyncOptions
    {
        /// <summary>The date fees are charged from for THIS student (Add Existing Student's "Charge fees from"). Null = their enrolment date. Never before today unless <see cref="Backdate"/>.</summary>
        public DateOnly? ChargeFrom { get; set; }
        /// <summary>The explicit, confirmed back-dating: dues dated before today (from <see cref="ChargeFrom"/> on) are created as BACKDATED and logged.</summary>
        public bool Backdate { get; set; }
        public string? BackdatingReason { get; set; }
        /// <summary>The signed-in user doing the back-dating (from the token), for the log.</summary>
        public long? UserId { get; set; }
        /// <summary>Fee types whose line's effective date was just moved EARLIER: moving it earlier never creates dues by itself, so the sync only updates what exists for them.</summary>
        public IReadOnlySet<long>? NoNewDuesForFeeTypes { get; set; }
    }

    /// <summary>What a fee sync created that needs reporting.</summary>
    public class FeeSyncResult
    {
        public int BackdatedCount { get; set; }
        public decimal BackdatedAmount { get; set; }
    }
}
