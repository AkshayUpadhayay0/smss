using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>
    /// Parent accounts: creation (at Confirm Admission and in bulk), the one-time set-password / reset links, resend, admin reset and the public recovery endpoints. Everything that messages a parent goes through
    /// IParentMessageDispatcher (WhatsApp and email, logged), and NO password, token or link is ever returned, logged or stored in the log. A guardian's school always comes from the caller's token (or, for the public
    /// endpoints, from the token / contact itself), never from the request.
    /// </summary>
    public interface IParentAccountService
    {
        /// <summary>
        /// Called after an admission is COMMITTED. Creates the primary guardian's account when there is none, there is consent on record and at least one channel, and sends the set-password link; a guardian who already has an
        /// account gets the "child added" message and no second account. Never throws and never fails the admission: every problem is in the returned result.
        /// </summary>
        Task<ParentAccountResultDto> OnAdmissionConfirmedAsync(string schoolId, long guardianId, string studentId, long actorUserId, CancellationToken ct);

        /// <summary>The account state and the recent deliveries of a guardian of this school (404 for another school's).</summary>
        Task<ApiResponse<ParentAccountStatusDto>> GetStatusAsync(string schoolId, long guardianId, CancellationToken ct);
        /// <summary>Sends a new set-password link (the older ones stop working). 409 when the parent has already chosen a password; 429 past the limit.</summary>
        Task<ApiResponse<ParentAccountResultDto>> ResendSetPasswordLinkAsync(string schoolId, long guardianId, long actorUserId, CancellationToken ct);
        /// <summary>The School Admin's reset: sends a reset link (the admin never sees or sets a password or link). 429 past the limit.</summary>
        Task<ApiResponse<ParentAccountResultDto>> AdminResetAsync(string schoolId, long guardianId, long actorUserId, CancellationToken ct);

        Task<ApiResponse<BulkParentAccountPreviewDto>> BulkPreviewAsync(string schoolId, CancellationToken ct);
        Task<ApiResponse<BulkParentAccountResultDto>> BulkCreateAsync(string schoolId, long actorUserId, BulkParentAccountRequest request, CancellationToken ct);

        // ---- public
        Task<ApiResponse<LinkCheckDto>> CheckLinkAsync(string? token, CancellationToken ct);
        /// <summary>Uses a set-password / reset link: sets the password, ends every session of that user and every other unused link. The same 400 answer for every kind of unusable link.</summary>
        Task<ApiResponse<PasswordSetDto>> SetPasswordAsync(SetParentPasswordRequest request, CancellationToken ct);
        /// <summary>"Forgot id or password". ALWAYS the same 200 answer, whether or not the contact matches an account, and whatever limit was hit.</summary>
        Task<ApiResponse<object>> ForgotAsync(ForgotLoginRequest request, string clientKey, CancellationToken ct);
        /// <summary>The second half of "forgot": makes the reset link and sends it for the matched accounts. Called from the background queue AFTER the answer has gone; never throws.</summary>
        Task SendRecoveryMessagesAsync(IReadOnlyList<smss_api_db_layer.@interface.AccountContactRow> rows, string? whatsApp, string? email, CancellationToken ct);
    }

    /// <summary>Work to do after the response: a plain in-process queue (no new infrastructure). Each item runs in its own DI scope; a failure is logged and never reaches a caller.</summary>
    public interface IBackgroundTaskQueue
    {
        void Enqueue(Func<IServiceProvider, CancellationToken, Task> work);
    }

    /// <summary>The per-contact limiter for the public recovery endpoint (in memory, per API instance): a contact can ask only a few times an hour, whatever address the requests come from.</summary>
    public interface IContactThrottle
    {
        /// <summary>True when this contact may be looked up now (and counts it).</summary>
        bool Allow(string contactKey, int max = 5);
    }
}
