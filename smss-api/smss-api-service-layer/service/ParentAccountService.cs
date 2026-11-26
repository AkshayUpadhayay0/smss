using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>In-memory limiter for the public recovery endpoint: at most <c>max</c> lookups per key per hour (a contact, or a client address). Per API instance; the per-guardian link limit in the database is the shared backstop.</summary>
    public class ContactThrottle : IContactThrottle
    {
        private static readonly TimeSpan Window = TimeSpan.FromHours(1);
        private const int MaxKeys = 50000;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

        public bool Allow(string contactKey, int max = 5)
        {
            var now = DateTime.UtcNow;
            if (_hits.Count > MaxKeys) _hits.Clear();   // a flood of distinct keys cannot grow this without limit
            var q = _hits.GetOrAdd(contactKey, _ => new Queue<DateTime>());
            lock (q)
            {
                while (q.Count > 0 && now - q.Peek() > Window) q.Dequeue();
                if (q.Count >= max) return false;
                q.Enqueue(now);
                return true;
            }
        }
    }

    public class ParentAccountService : IParentAccountService
    {
        public const int BulkMaxPerRun = 100;
        private const string GenericForgot = "If that matches a parent account, we have sent the login ID and a link to choose a new password to the registered WhatsApp number or email.";
        private const string BadLink = "This link is not valid any more. Ask the school for a new one, or use \"Forgot ID or password\".";

        private readonly IGuardianRepository _guardians;
        private readonly IParentAccountRepository _accounts;
        private readonly IMessagingRepository _messaging;
        private readonly IGuardianLoginIdService _loginIds;
        private readonly IUserActionTokenService _tokens;
        private readonly IParentMessageDispatcher _dispatcher;
        private readonly IAuthRepository _auth;
        private readonly INotificationService _notify;
        private readonly IContactThrottle _throttle;
        private readonly ParentAccountsOptions _opt;
        private readonly ILogger<ParentAccountService> _logger;

        public ParentAccountService(IGuardianRepository guardians, IParentAccountRepository accounts, IMessagingRepository messaging, IGuardianLoginIdService loginIds, IUserActionTokenService tokens,
            IParentMessageDispatcher dispatcher, IAuthRepository auth, INotificationService notify, IContactThrottle throttle, IOptions<ParentAccountsOptions> options, ILogger<ParentAccountService> logger)
        {
            _guardians = guardians; _accounts = accounts; _messaging = messaging; _loginIds = loginIds; _tokens = tokens; _dispatcher = dispatcher; _auth = auth; _notify = notify; _throttle = throttle;
            _opt = options.Value; _logger = logger;
        }

        // ------------------------------------------------------------------------------------------------ rules
        private static bool HasWhatsApp(TbGuardian g) => !string.IsNullOrWhiteSpace(g.WhatsAppNumber);
        private static bool HasEmail(TbGuardian g) => !string.IsNullOrWhiteSpace(g.Email) && PhoneNumbers.EmailError(g.Email, "x") == null;
        private static bool Complete(string? whatsApp, string? email) => !string.IsNullOrWhiteSpace(whatsApp) && !string.IsNullOrWhiteSpace(email) && PhoneNumbers.EmailError(email, "x") == null;
        private static string FullName(TbGuardian g) => string.Join(' ', new[] { g.FirstName, g.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        private static string Name(string? first, string? last) => string.Join(' ', new[] { first, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        /// <summary>Why a message / account cannot be made for this guardian, or null when it can: Active, not merged, consent on record, and at least one channel.</summary>
        private static string? NotAllowedReason(TbGuardian g, int activeId)
        {
            if (g.StatusId != activeId || g.MergedIntoGuardianId != null) return "The guardian is not Active (merged or inactive).";
            if (g.ConsentGivenAt == null) return "No parent consent is on record for this guardian. Tick the consent on the admission form, or have the School Admin confirm it for existing parents.";
            if (!HasWhatsApp(g) && !HasEmail(g)) return "The guardian has no WhatsApp number or email on file. Add one on the Parent details screen.";
            return null;
        }

        private static List<ParentDeliveryDto> ToDtos(ParentMessage m, IReadOnlyList<DeliveryOutcome> outcomes) => outcomes.Select(o => new ParentDeliveryDto
        {
            DeliveryId = o.DeliveryId ?? 0, Channel = o.Channel, Status = o.Status, ErrorCode = o.ErrorCode, TemplateCode = m.TemplateCode, Purpose = m.Purpose, SenderKind = "", CreatedAt = DateTime.UtcNow,
        }).ToList();

        private static ParentDeliveryDto ToDto(TbMessageDelivery d) => new()
        {
            DeliveryId = d.DeliveryId, Channel = d.Channel, Status = d.Status, TemplateCode = d.TemplateCode, Purpose = d.Purpose, RecipientMasked = d.RecipientMasked, SenderKind = d.SenderKind,
            ErrorCode = d.ErrorCode, ErrorMessage = d.ErrorMessage, CreatedAt = d.CreatedAt,
        };

        private static ParentAccountResultDto Result(string outcome, long? guardianId, string? reason = null, string? loginId = null) => new() { Outcome = outcome, GuardianId = guardianId, Reason = reason, LoginId = loginId };

        private static ApiResponse<T> Reply<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        // ------------------------------------------------------------------------------------------------ admission and creation
        public async Task<ParentAccountResultDto> OnAdmissionConfirmedAsync(string schoolId, long guardianId, string studentId, long actorUserId, CancellationToken ct)
        {
            try
            {
                var g = await _guardians.GetGuardianAsync(schoolId, guardianId, false, ct);
                if (g == null) return Result("NOT_CREATED", guardianId, "The guardian was not found.");
                return g.UserId != null ? await ChildAddedAsync(schoolId, g, studentId, actorUserId, ct) : await CreateAsync(schoolId, guardianId, studentId, actorUserId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("The parent account for guardian {GuardianId} could not be created after the admission ({Error})", guardianId, ex.GetType().Name);
                var r = Result("FAILED", guardianId, "The parent account could not be created. The admission itself is saved; use Resend or the Parent details screen to try again.");
                r.CanResend = true;
                return r;
            }
        }

        /// <summary>Creates the account (one transaction under a row lock on the guardian) and then, after the commit, sends the set-password link.</summary>
        private async Task<ParentAccountResultDto> CreateAsync(string schoolId, long guardianId, string? studentId, long actor, CancellationToken ct)
        {
            var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
            var roleId = await _accounts.GetRoleIdByNameAsync(RoleNames.Guardian, ct);
            if (activeId == null || roleId == null) { _logger.LogError("Parent accounts: the Active status or the Guardian role is missing"); return Result("FAILED", guardianId, "The system is not configured for parent accounts (the Guardian role is missing)."); }

            TbGuardian g; TbUsers user;
            await using (var tx = await _guardians.BeginTransactionAsync(ct))
            {
                var locked = (await _guardians.LockGuardiansAsync(schoolId, new[] { guardianId }, ct)).SingleOrDefault();
                if (locked == null) return Result("NOT_CREATED", guardianId, "The guardian was not found.");
                g = locked;
                if (g.UserId != null) { await tx.RollbackAsync(ct); return studentId != null ? await ChildAddedAsync(schoolId, g, studentId, actor, ct) : Result("NOT_CREATED", guardianId, "This guardian already has a parent account."); }
                var reason = NotAllowedReason(g, activeId.Value);
                if (reason != null) return Result("NOT_CREATED", guardianId, reason);
                var loginId = await _loginIds.AllocateAsync(schoolId, ct);
                if (loginId == null) return Result("NOT_CREATED", guardianId, "This school has no short code, so no login id can be made. Contact support.");

                var now = DateTime.UtcNow;
                user = new TbUsers
                {
                    SchoolId = schoolId, UserType = UserTypes.Guardian, OrgUserId = loginId, DisplayName = FullName(g),
                    Email = HasEmail(g) ? g.Email!.Trim() : null, MobileNumber = g.MobileNumber,
                    PasswordHash = null,   // no password exists until the parent chooses one through the link: the account cannot sign in before that
                    IsFirstLogin = true, StatusId = activeId, CreatedAt = now, UpdatedAt = now,
                    UserRoles = { new TbUserRoles { RoleId = roleId.Value, CreatedAt = now } },
                };
                await _accounts.AddUserAsync(user, ct);
                g.UserId = user.UserId; g.UpdatedAt = now;
                await _guardians.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            // the account is committed: from here a problem never undoes it
            try { return await IssueAndSendAsync(schoolId, g, user, ActionTokenPurposes.SetPassword, studentId, actor, "CREATED", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("The link for the new parent account of guardian {GuardianId} could not be sent ({Error})", guardianId, ex.GetType().Name);
                var r = Result("CREATED", guardianId, "The account was created but the link could not be sent. Use Resend.", user.OrgUserId);
                r.CanResend = true;
                return r;
            }
        }

        /// <summary>The guardian already has an account: tell them the child was added (no second account; the child shows up on the family screen by itself).</summary>
        private async Task<ParentAccountResultDto> ChildAddedAsync(string schoolId, TbGuardian g, string studentId, long actor, CancellationToken ct)
        {
            var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? 0;
            var user = g.UserId == null ? null : await _accounts.GetUserAsync(g.UserId.Value, false, ct);
            if (user == null || user.StatusId != activeId) return Result("NOT_CREATED", g.GuardianId, "The guardian's login is not Active, so no message was sent.");
            var reason = NotAllowedReason(g, activeId);
            if (reason != null) return Result("NOT_CREATED", g.GuardianId, reason, user.OrgUserId);
            var schoolName = await _accounts.GetSchoolNameAsync(schoolId, ct) ?? "your school";
            var child = await _accounts.GetStudentNameAsync(schoolId, studentId, ct) ?? "your child";
            var name = FullName(g);
            var message = new ParentMessage(schoolId, g.GuardianId, user.UserId, ParentTemplateCodes.ChildAdded, MessagePurposes.ChildAdded,
                HasWhatsApp(g) ? g.WhatsAppNumber : null, HasWhatsApp(g) ? ParentTemplates.ChildAdded(name, child, schoolName, user.OrgUserId!) : null,
                HasEmail(g) ? g.Email : null, HasEmail(g) ? EmailTemplates.ParentChildAdded(name, child, schoolName, user.OrgUserId!, _notify.LoginUrl) : null, actor);
            var outcomes = await _dispatcher.SendAsync(message, ct);
            var result = Result("CHILD_ADDED", g.GuardianId, null, user.OrgUserId);
            result.Deliveries = ToDtos(message, outcomes);
            return result;
        }

        /// <summary>Makes a new link (the older ones stop working) and sends it on the channels the guardian has.</summary>
        private async Task<ParentAccountResultDto> IssueAndSendAsync(string schoolId, TbGuardian g, TbUsers user, string purpose, string? studentId, long? actor, string outcome, CancellationToken ct)
        {
            var activeStudent = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? 0;
            var schoolName = await _accounts.GetSchoolNameAsync(schoolId, ct) ?? "your school";
            var child = (studentId != null ? await _accounts.GetStudentNameAsync(schoolId, studentId, ct) : null) ?? await _accounts.GetPrimaryChildNameAsync(schoolId, g.GuardianId, activeStudent, ct) ?? "your child";
            var issued = await _tokens.IssueAsync(user.UserId, schoolId, purpose, actor, ct);
            var link = _tokens.BuildLink(purpose, issued.Token);
            var name = FullName(g);
            var loginId = user.OrgUserId!;
            ParentMessage message;
            string? warning = null;
            if (purpose == ActionTokenPurposes.SetPassword)
            {
                message = new ParentMessage(schoolId, g.GuardianId, user.UserId, ParentTemplateCodes.AccountReady, MessagePurposes.SetPassword,
                    HasWhatsApp(g) ? g.WhatsAppNumber : null, HasWhatsApp(g) ? ParentTemplates.AccountReady(name, child, schoolName, loginId, _opt.SetPasswordLinkHours, issued.Token) : null,
                    HasEmail(g) ? g.Email : null, HasEmail(g) && link != null ? EmailTemplates.ParentAccountReady(name, child, schoolName, loginId, link, _opt.SetPasswordLinkHours) : null, actor);
            }
            else
            {
                message = new ParentMessage(schoolId, g.GuardianId, user.UserId, ParentTemplateCodes.LoginHelp, MessagePurposes.ResetPassword,
                    HasWhatsApp(g) ? g.WhatsAppNumber : null, HasWhatsApp(g) ? ParentTemplates.LoginHelp(name, schoolName, loginId, _opt.ResetLinkMinutes, issued.Token) : null,
                    HasEmail(g) ? g.Email : null, HasEmail(g) && link != null ? EmailTemplates.ParentLoginHelp(name, schoolName, loginId, link, _opt.ResetLinkMinutes) : null, actor);
            }
            if (link == null && HasEmail(g)) warning = "The website address (ParentAccounts:WebBaseUrl) is not set, so the email has no link.";
            var outcomes = await _dispatcher.SendAsync(message, ct);
            var result = Result(outcome, g.GuardianId, warning, loginId);
            result.Deliveries = ToDtos(message, outcomes);
            result.CanResend = true;
            return result;
        }

        // ------------------------------------------------------------------------------------------------ staff actions
        public async Task<ApiResponse<ParentAccountStatusDto>> GetStatusAsync(string schoolId, long guardianId, CancellationToken ct)
        {
            try
            {
                var g = await _guardians.GetGuardianAsync(schoolId, guardianId, false, ct);
                if (g == null) return Reply<ParentAccountStatusDto>(404, "Guardian not found.", null);
                var user = g.UserId == null ? null : await _accounts.GetUserAsync(g.UserId.Value, false, ct);
                var since = DateTime.UtcNow.AddHours(-24);
                var links = user == null ? 0 : await _messaging.CountTokensSinceAsync(user.UserId, ActionTokenPurposes.SetPassword, since, ct) + await _messaging.CountTokensSinceAsync(user.UserId, ActionTokenPurposes.ResetPassword, since, ct);
                var deliveries = await _accounts.GetRecentDeliveriesAsync(schoolId, guardianId, 10, ct);
                return Reply(200, "Successfully fetched", new ParentAccountStatusDto
                {
                    GuardianId = g.GuardianId, GuardianName = FullName(g), HasAccount = user != null, LoginId = user?.OrgUserId, PasswordSet = user?.PasswordHash != null,
                    ConsentRecorded = g.ConsentGivenAt != null, WhatsAppOnFile = HasWhatsApp(g), EmailOnFile = HasEmail(g), LinksInLast24Hours = links, MaxLinksPer24Hours = _opt.MaxLinksPer24Hours,
                    Deliveries = deliveries.Select(ToDto).ToList(),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<ParentAccountStatusDto>(ex, "reading a parent account"); }
        }

        public Task<ApiResponse<ParentAccountResultDto>> ResendSetPasswordLinkAsync(string schoolId, long guardianId, long actorUserId, CancellationToken ct) =>
            SendLinkAsync(schoolId, guardianId, actorUserId, ActionTokenPurposes.SetPassword, "RESENT", ct);

        public Task<ApiResponse<ParentAccountResultDto>> AdminResetAsync(string schoolId, long guardianId, long actorUserId, CancellationToken ct) =>
            SendLinkAsync(schoolId, guardianId, actorUserId, ActionTokenPurposes.ResetPassword, "RESENT", ct);

        private async Task<ApiResponse<ParentAccountResultDto>> SendLinkAsync(string schoolId, long guardianId, long actor, string purpose, string outcome, CancellationToken ct)
        {
            try
            {
                var g = await _guardians.GetGuardianAsync(schoolId, guardianId, false, ct);
                if (g == null) return Reply<ParentAccountResultDto>(404, "Guardian not found.", null);
                if (g.UserId == null) return Reply<ParentAccountResultDto>(409, "This guardian has no parent account yet.", null);
                var user = await _accounts.GetUserAsync(g.UserId.Value, false, ct);
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? 0;
                if (user == null || user.StatusId != activeId) return Reply<ParentAccountResultDto>(409, "The guardian's login is not Active.", null);
                var reason = NotAllowedReason(g, activeId);
                if (reason != null) return Reply<ParentAccountResultDto>(409, reason, null);
                var passwordSet = user.PasswordHash != null;
                if (purpose == ActionTokenPurposes.SetPassword && passwordSet) return Reply<ParentAccountResultDto>(409, "This parent has already chosen a password. Use Reset to send a reset link.", null);
                if (purpose == ActionTokenPurposes.ResetPassword && !passwordSet) return Reply<ParentAccountResultDto>(409, "This parent has not chosen a password yet. Use Resend to send the set-password link again.", null);
                var count = await _messaging.CountTokensSinceAsync(user.UserId, purpose, DateTime.UtcNow.AddHours(-24), ct);
                if (count >= _opt.MaxLinksPer24Hours) return Reply<ParentAccountResultDto>(429, $"Links have already been sent {count} times in the last 24 hours (the first one and {_opt.ResendLimitPer24Hours} resends). Try again later.", null);
                var result = await IssueAndSendAsync(schoolId, g, user, purpose, null, actor, outcome, ct);
                return Reply(200, "A new link was sent.", result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<ParentAccountResultDto>(ex, "sending a parent link"); }
        }

        // ------------------------------------------------------------------------------------------------ bulk creation
        public async Task<ApiResponse<BulkParentAccountPreviewDto>> BulkPreviewAsync(string schoolId, CancellationToken ct)
        {
            try
            {
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return Reply<BulkParentAccountPreviewDto>(500, "The system is not configured correctly. Please contact support.", null);
                var rows = await _accounts.ListAccountlessGuardiansAsync(schoolId, activeId.Value, ct);
                var complete = rows.Where(r => Complete(r.WhatsApp, r.Email)).ToList();
                return Reply(200, "Successfully fetched", new BulkParentAccountPreviewDto
                {
                    Eligible = complete.Count, WillCreate = Math.Min(complete.Count, BulkMaxPerRun), MaxPerRun = BulkMaxPerRun,
                    IncompleteDetails = rows.Count - complete.Count, WithoutConsentOnRecord = complete.Count(r => r.ConsentGivenAt == null),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<BulkParentAccountPreviewDto>(ex, "previewing parent accounts"); }
        }

        public async Task<ApiResponse<BulkParentAccountResultDto>> BulkCreateAsync(string schoolId, long actorUserId, BulkParentAccountRequest request, CancellationToken ct)
        {
            try
            {
                if (!request.ConfirmSchoolHoldsParentConsent) return Reply<BulkParentAccountResultDto>(400, "Confirm that the school holds the parents' consent to be contacted before accounts are created.", null);
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return Reply<BulkParentAccountResultDto>(500, "The system is not configured correctly. Please contact support.", null);
                var rows = (await _accounts.ListAccountlessGuardiansAsync(schoolId, activeId.Value, ct)).Where(r => Complete(r.WhatsApp, r.Email)).ToList();
                var run = rows.Take(BulkMaxPerRun).ToList();
                var result = new BulkParentAccountResultDto { Remaining = rows.Count - run.Count };
                foreach (var row in run)
                {
                    result.Attempted++;
                    ParentAccountResultDto one;
                    try
                    {
                        if (row.ConsentGivenAt == null)
                        {
                            // the School Admin's confirmation is the consent on record for this guardian: who, when and how
                            var g = await _guardians.GetGuardianAsync(schoolId, row.GuardianId, true, ct);
                            if (g != null && g.ConsentGivenAt == null)
                            {
                                g.ConsentGivenAt = DateTime.UtcNow; g.ConsentRecordedBy = actorUserId; g.ConsentSource = "BULK_CONFIRMED"; g.UpdatedAt = DateTime.UtcNow;
                                await _guardians.SaveChangesAsync(ct);
                            }
                        }
                        one = await CreateAsync(schoolId, row.GuardianId, null, actorUserId, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError("Bulk parent accounts: guardian {GuardianId} failed ({Error})", row.GuardianId, ex.GetType().Name);
                        one = Result("FAILED", row.GuardianId, "The account could not be created.");
                    }
                    if (one.Outcome == "CREATED") result.Created++; else result.NotCreated++;
                    result.Items.Add(new BulkParentAccountItemDto { GuardianId = row.GuardianId, Name = row.Name, Outcome = one.Outcome, Reason = one.Reason });
                }
                _logger.LogInformation("Bulk parent accounts for school {SchoolId} by user {UserId}: {Created} created, {NotCreated} not created, {Remaining} remaining; school consent confirmed", schoolId, actorUserId, result.Created, result.NotCreated, result.Remaining);
                return Reply(200, $"{result.Created} parent account(s) created.", result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<BulkParentAccountResultDto>(ex, "creating parent accounts in bulk"); }
        }

        // ------------------------------------------------------------------------------------------------ public
        public async Task<ApiResponse<LinkCheckDto>> CheckLinkAsync(string? token, CancellationToken ct)
        {
            try
            {
                var ok = await _tokens.IsUsableAsync(token, ActionTokenPurposes.SetPassword, ct) || await _tokens.IsUsableAsync(token, ActionTokenPurposes.ResetPassword, ct);
                return Reply(200, "Checked", new LinkCheckDto { Valid = ok });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<LinkCheckDto>(ex, "checking a link"); }
        }

        public async Task<ApiResponse<PasswordSetDto>> SetPasswordAsync(SetParentPasswordRequest request, CancellationToken ct)
        {
            try
            {
                // a weak password is refused BEFORE the link is used up
                if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8 || request.NewPassword.Length > 100) return Reply<PasswordSetDto>(400, "The password must be 8 to 100 characters.", null);
                if (!UserActionTokens.LooksValid(request.Token)) return Reply<PasswordSetDto>(400, BadLink, null);
                // everything that can be refused is checked BEFORE the link is used up, so a refusal never burns it
                var peek = await _messaging.PeekTokenAsync(UserActionTokens.Hash(request.Token), ct);
                if (peek == null || peek.UsedAt != null || peek.RevokedAt != null || peek.ExpiresAt <= peek.DbNow || !ActionTokenPurposes.IsKnown(peek.Purpose)) return Reply<PasswordSetDto>(400, BadLink, null);
                var user = await _accounts.GetUserAsync(peek.UserId, true, ct);
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? 0;
                // only a parent's or a student's login can be set this way; an inactive login, or a guardian who was merged away or made inactive, cannot
                if (user == null || user.StatusId != activeId || (user.UserType != UserTypes.Guardian && user.UserType != UserTypes.Student) || string.IsNullOrEmpty(user.OrgUserId)) return Reply<PasswordSetDto>(400, BadLink, null);
                if (user.UserType == UserTypes.Guardian)
                {
                    var info = await _accounts.GetGuardianByUserIdAsync(user.UserId, ct);
                    if (info == null || info.StatusId != activeId || info.MergedIntoGuardianId != null) return Reply<PasswordSetDto>(400, BadLink, null);
                }
                if (string.Equals(request.NewPassword, user.OrgUserId, StringComparison.OrdinalIgnoreCase)) return Reply<PasswordSetDto>(400, "The password cannot be the same as the login ID.", null);

                // now use it: one atomic statement, so two requests with the same link cannot both succeed
                var link = await _tokens.ConsumeAsync(request.Token, peek.Purpose, ct);
                if (link == null || link.UserId != user.UserId) return Reply<PasswordSetDto>(400, BadLink, null);

                var now = DateTime.UtcNow;
                user.PasswordHash = PasswordHelper.Hash(request.NewPassword);
                user.IsFirstLogin = false; user.UpdatedAt = now;
                await _auth.SaveChangesAsync();
                await _auth.RevokeAllRefreshTokensAsync(user.UserId, now);        // every existing session ends
                await _messaging.RevokeTokensAsync(user.UserId, null, ct);        // and every other unused link
                return Reply(200, "Your password is set. You can now sign in with your login ID.", new PasswordSetDto { LoginId = user.OrgUserId });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Failure<PasswordSetDto>(ex, "setting a parent password"); }
        }

        public async Task<ApiResponse<object>> ForgotAsync(ForgotLoginRequest request, string clientKey, CancellationToken ct)
        {
            var generic = Reply<object>(200, GenericForgot, new { });
            try
            {
                var contact = (request.Contact ?? "").Trim();
                string? whatsApp = null, email = null;
                if (contact.Contains('@')) { if (PhoneNumbers.EmailError(contact, "x") == null) email = contact.ToLowerInvariant(); }
                else whatsApp = PhoneNumbers.Normalise(contact);
                if (whatsApp == null && email == null) return generic;
                if (!_throttle.Allow("contact:" + (whatsApp ?? email), 5) || !_throttle.Allow("ip:" + clientKey, 20)) return generic;   // the same answer: a limit is never visible

                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return generic;
                var rows = await _accounts.FindAccountsByContactAsync(whatsApp, email, activeId.Value, ct);   // every school this contact is a parent in: each gets its own message
                foreach (var row in rows)
                {
                    try
                    {
                        var user = await _accounts.GetUserAsync(row.UserId, false, ct);
                        if (user == null || user.PasswordHash == null) continue;   // an account that never chose a password gets its set-password link from the school (resend), not from here
                        var count = await _messaging.CountTokensSinceAsync(row.UserId, ActionTokenPurposes.ResetPassword, DateTime.UtcNow.AddHours(-24), ct);
                        if (count >= _opt.MaxLinksPer24Hours) continue;
                        var issued = await _tokens.IssueAsync(row.UserId, row.SchoolId, ActionTokenPurposes.ResetPassword, null, ct);
                        var link = _tokens.BuildLink(ActionTokenPurposes.ResetPassword, issued.Token);
                        // only the channel the contact was typed for: a number gets WhatsApp, an email address gets email
                        var message = new ParentMessage(row.SchoolId, row.GuardianId, row.UserId, ParentTemplateCodes.LoginHelp, MessagePurposes.ResetPassword,
                            whatsApp != null ? row.WhatsApp : null, whatsApp != null ? ParentTemplates.LoginHelp(row.Name, row.SchoolName, row.LoginId, _opt.ResetLinkMinutes, issued.Token) : null,
                            email != null ? row.Email : null, email != null && link != null ? EmailTemplates.ParentLoginHelp(row.Name, row.SchoolName, row.LoginId, link, _opt.ResetLinkMinutes) : null, null);
                        await _dispatcher.SendAsync(message, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning("A recovery message for guardian {GuardianId} could not be sent ({Error})", row.GuardianId, ex.GetType().Name); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning("Forgot id or password failed ({Error})", ex.GetType().Name); }
            return generic;
        }

        private ApiResponse<T> Failure<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Reply<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
