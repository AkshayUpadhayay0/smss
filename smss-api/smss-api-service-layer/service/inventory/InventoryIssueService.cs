using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Issue to staff / student / department, returns and voids. Status is derived from the line quantities, never stored.</summary>
    public class InventoryIssueService : InventoryServiceBase, IInventoryIssueService
    {
        private const string NumberIndex = "uq_tb_inv_issues_school_number";
        private readonly IStockLedgerService _ledger;

        public InventoryIssueService(IInventoryRepository repo, IStockLedgerService ledger, ILogger<InventoryIssueService> logger) : base(repo, logger) => _ledger = ledger;

        private static InvIssueDto ToDto(InvIssueRow r) => new()
        {
            IssueId = r.Issue.IssueId, IssueNumber = r.Issue.IssueNumber, RecipientType = r.Issue.RecipientType,
            RecipientId = r.Issue.EmployeeId ?? r.Issue.StudentId ?? r.Issue.DepartmentId?.ToString() ?? "", RecipientName = r.RecipientName,
            IssueDate = r.Issue.IssueDate, Purpose = r.Issue.Purpose, IssuedByName = r.IssuedByName,
            Status = InvIssueStatuses.Of(r.Issue.Status, r.TotalQuantity, r.ReturnedQuantity), TotalQuantity = r.TotalQuantity, ReturnedQuantity = r.ReturnedQuantity,
            VoidedByName = r.VoidedByName, VoidedAt = r.Issue.VoidedAt, VoidReason = r.Issue.VoidReason, CreatedAt = r.Issue.CreatedAt
        };

        private async Task<InvIssueDto?> LoadAsync(string schoolId, long issueId, CancellationToken ct)
        {
            var row = await Repo.GetIssueRowAsync(schoolId, issueId, ct);
            if (row == null) return null;
            var dto = ToDto(row);
            var lines = await Repo.GetIssueLinesAsync(schoolId, new[] { issueId }, ct);
            var returns = await Repo.GetReturnsAsync(schoolId, lines.Select(l => l.Line.IssueLineId).ToList(), ct);
            dto.Lines = lines.Select(l => new InvIssueLineDto
            {
                IssueLineId = l.Line.IssueLineId, ItemId = l.Line.ItemId, ItemCode = l.ItemCode, ItemName = l.ItemName, UnitCode = l.UnitCode,
                Quantity = l.Line.Quantity, ReturnedQuantity = l.Line.ReturnedQuantity, OutstandingQuantity = l.Line.Quantity - l.Line.ReturnedQuantity,
                Returns = returns.Where(x => x.IssueLineId == l.Line.IssueLineId).Select(x => new InvReturnDto
                {
                    ReturnId = x.Row.Return.ReturnId, Quantity = x.Row.Return.Quantity, ReturnDate = x.Row.Return.ReturnDate, Condition = x.Row.Return.Condition,
                    Remarks = x.Row.Return.Remarks, ReceivedByName = x.Row.ReceivedByName
                }).ToList()
            }).ToList();
            return dto;
        }

        public async Task<ApiResponse<InvPageDto<InvIssueDto>>> GetIssuesAsync(string? schoolId, bool canManage, string? recipientType, string? status, string? search, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvPageDto<InvIssueDto>>();
            try
            {
                var type = InventoryRules.CleanUpper(recipientType);
                if (type != null && !InvRecipientTypes.All.Contains(type)) return Result<InvPageDto<InvIssueDto>>(400, "Recipient type must be STAFF, STUDENT or DEPARTMENT.", null);
                var derived = InventoryRules.Clean(status);
                string?[] known = { InvIssueStatuses.Outstanding, InvIssueStatuses.PartiallyReturned, InvIssueStatuses.Returned, InvIssueStatuses.Voided };
                if (derived != null && !known.Contains(derived)) return Result<InvPageDto<InvIssueDto>>(400, "Unknown status.", null);
                if (from != null && to != null && to < from) return Result<InvPageDto<InvIssueDto>>(400, "The 'to' date can't be before the 'from' date.", null);
                var p = InventoryRules.Page(page); var size = InventoryRules.PageSize(pageSize);
                var (rows, total) = await Repo.GetIssuesAsync(schoolId, new InvIssueFilter { RecipientType = type, DerivedStatus = derived, Search = search, From = from, To = to }, p, size, ct);
                return Result(200, "Successfully fetched", new InvPageDto<InvIssueDto> { CanManage = canManage, Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = p, PageSize = size });
            }
            catch (Exception ex) { return Error<InvPageDto<InvIssueDto>>(ex, "listing issues"); }
        }

        public async Task<ApiResponse<InvIssueDto>> GetIssueAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvIssueDto>();
            try
            {
                var dto = await LoadAsync(schoolId, id, ct);
                return dto == null ? Result<InvIssueDto>(404, "Issue not found.", null) : Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<InvIssueDto>(ex, "reading an issue"); }
        }

        public async Task<ApiResponse<InvIssueDto>> CreateIssueAsync(string? schoolId, long userId, CreateInvIssueRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvIssueDto>();
            try
            {
                var type = InventoryRules.CleanUpper(req.RecipientType) ?? "";
                if (!InvRecipientTypes.All.Contains(type)) return Result<InvIssueDto>(400, "Recipient type must be STAFF, STUDENT or DEPARTMENT.", null);
                var recipientId = InventoryRules.Clean(req.RecipientId);
                if (recipientId == null) return Result<InvIssueDto>(400, "A recipient is required.", null);
                var issueDate = req.IssueDate!.Value;
                if (issueDate > SchoolClock.Today()) return Result<InvIssueDto>(400, "The issue date can't be in the future.", null);
                var purpose = InventoryRules.Clean(req.Purpose);

                var lines = req.Lines!;
                if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count) return Result<InvIssueDto>(400, "An item can appear only once in an issue. Add the quantities together.", null);
                foreach (var l in lines) if (InventoryRules.QuantityError(l.Quantity!.Value) is { } qe) return Result<InvIssueDto>(400, qe, null);

                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvIssueDto>();

                // the recipient: this school's own, and Active (another school's id is a plain 404)
                var recipient = await Repo.GetActiveRecipientAsync(schoolId, type, recipientId, active.Value, ct);
                if (recipient == null) return Result<InvIssueDto>(404, "The recipient was not found, or is not active.", null);

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await using var tx = await Repo.BeginTransactionAsync(ct);
                    try
                    {
                        // items: this school's own and active; a stable order so two issues touching the same items never wait on each other in a circle
                        var ordered = lines.OrderBy(l => l.ItemId).ToList();
                        foreach (var l in ordered)
                        {
                            var item = await Repo.GetItemAsync(schoolId, l.ItemId!.Value, ct);
                            if (item == null) return Result<InvIssueDto>(404, "Item not found.", null);
                            if (item.StatusId != active) return Result<InvIssueDto>(400, $"The item \"{item.ItemName}\" is deactivated and can't be issued.", null);
                        }

                        await Repo.LockNumbersAsync("issue", schoolId, ct);
                        var prefix = $"ISS-{SchoolClock.Today().Year}-";
                        var next = await Repo.GetMaxIssueSequenceAsync(schoolId, prefix, ct) + 1;
                        var now = DateTime.UtcNow;
                        var issue = new TbInvIssue
                        {
                            SchoolId = schoolId, IssueNumber = $"{prefix}{next:D4}", RecipientType = type, IssueDate = issueDate, Purpose = purpose,
                            EmployeeId = type == InvRecipientTypes.Staff ? recipient.Id : null,
                            StudentId = type == InvRecipientTypes.Student ? recipient.Id : null,
                            DepartmentId = type == InvRecipientTypes.Department ? long.Parse(recipient.Id) : null,
                            IssuedBy = userId, Status = PaymentStatuses.Active, CreatedAt = now
                        };
                        await Repo.AddAsync(issue, ct);
                        await Repo.SaveChangesAsync(ct);

                        var lineEntities = new List<TbInvIssueLine>();
                        foreach (var l in ordered)
                        {
                            var line = new TbInvIssueLine { IssueId = issue.IssueId, SchoolId = schoolId, ItemId = l.ItemId!.Value, Quantity = l.Quantity!.Value, ReturnedQuantity = 0 };
                            await Repo.AddAsync(line, ct);
                            lineEntities.Add(line);
                        }
                        await Repo.SaveChangesAsync(ct);

                        foreach (var line in lineEntities)
                        {
                            var change = await _ledger.ApplyAsync(schoolId, line.ItemId, -line.Quantity, InvMovementTypes.Issue, userId,
                                new StockChangeInfo { ReferenceType = InvReferenceTypes.IssueLine, ReferenceId = line.IssueLineId, Remarks = purpose }, ct);
                            if (!change.Ok)   // leaving the scope rolls the whole issue back: nothing is written
                                return change.ItemNotFound
                                    ? Result<InvIssueDto>(404, "Item not found.", null)
                                    : Result<InvIssueDto>(409, $"Not enough stock of \"{change.ItemName}\": {change.Available:0.###} available, {line.Quantity:0.###} requested. Nothing was issued.", null);
                        }
                        await tx.CommitAsync(ct);
                        return Result(201, $"Issued as {issue.IssueNumber}.", (await LoadAsync(schoolId, issue.IssueId, ct))!);
                    }
                    catch (Exception ex) when (IsUnique(ex, NumberIndex) && attempt < 2) { Logger.LogWarning(ex, "Issue number collision for school {SchoolId}; retrying", schoolId); Repo.ClearTracking(); }
                }
                return Result<InvIssueDto>(409, "Could not allocate an issue number. Nothing was issued. Please try again.", null);
            }
            catch (Exception ex) { return Error<InvIssueDto>(ex, "creating an issue"); }
        }

        public async Task<ApiResponse<InvIssueDto>> ReturnAsync(string? schoolId, long userId, long id, InvReturnRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvIssueDto>();
            try
            {
                var returnDate = req.ReturnDate!.Value;
                if (returnDate > SchoolClock.Today()) return Result<InvIssueDto>(400, "The return date can't be in the future.", null);
                var items = req.Lines!;
                if (items.Select(l => l.IssueLineId).Distinct().Count() != items.Count) return Result<InvIssueDto>(400, "A line can appear only once in a return.", null);
                foreach (var l in items)
                {
                    if (InventoryRules.QuantityError(l.Quantity!.Value) is { } qe) return Result<InvIssueDto>(400, qe, null);
                    var cond = InventoryRules.CleanUpper(l.Condition);
                    if (cond == null || !InvReturnConditions.All.Contains(cond)) return Result<InvIssueDto>(400, "Condition must be GOOD or DAMAGED.", null);
                    if (l.Remarks != null && l.Remarks.Length > InventoryRules.MaxRemarks) return Result<InvIssueDto>(400, "Remarks can be at most 500 characters.", null);
                }

                await using var tx = await Repo.BeginTransactionAsync(ct);
                var issue = await Repo.LockIssueAsync(schoolId, id, ct);   // serialises returns and voids of this issue
                if (issue == null) return Result<InvIssueDto>(404, "Issue not found.", null);
                if (issue.Status == PaymentStatuses.Voided) return Result<InvIssueDto>(409, "This issue has been voided, so nothing can be returned against it.", null);
                if (returnDate < issue.IssueDate) return Result<InvIssueDto>(400, "The return date can't be before the issue date.", null);

                var lines = await Repo.GetTrackedLinesAsync(schoolId, id, ct);
                foreach (var r in items.OrderBy(x => x.IssueLineId))
                {
                    var line = lines.FirstOrDefault(l => l.IssueLineId == r.IssueLineId!.Value);
                    if (line == null) return Result<InvIssueDto>(404, "Issue line not found.", null);
                    var qty = r.Quantity!.Value; var outstanding = line.Quantity - line.ReturnedQuantity;
                    if (qty > outstanding) return Result<InvIssueDto>(400, $"Only {outstanding:0.###} is still outstanding on this line, so {qty:0.###} can't be returned.", null);

                    var cond = InventoryRules.CleanUpper(r.Condition)!;
                    var entity = new TbInvIssueReturn
                    {
                        IssueLineId = line.IssueLineId, SchoolId = schoolId, Quantity = qty, ReturnDate = returnDate, Condition = cond,
                        Remarks = InventoryRules.Clean(r.Remarks), ReceivedBy = userId, CreatedAt = DateTime.UtcNow
                    };
                    await Repo.AddAsync(entity, ct);
                    line.ReturnedQuantity += qty;
                    await Repo.SaveChangesAsync(ct);   // gives the return its id for the ledger row

                    if (cond == InvReturnConditions.Good)   // a DAMAGED return is recorded, but no stock goes back
                    {
                        var change = await _ledger.ApplyAsync(schoolId, line.ItemId, qty, InvMovementTypes.Return, userId,
                            new StockChangeInfo { ReferenceType = InvReferenceTypes.IssueReturn, ReferenceId = entity.ReturnId, Remarks = entity.Remarks }, ct);
                        if (!change.Ok) return Result<InvIssueDto>(500, "Something went wrong. Please try again later.", null);
                    }
                }
                await tx.CommitAsync(ct);
                return Result(200, "Return recorded.", (await LoadAsync(schoolId, id, ct))!);
            }
            catch (Exception ex) { return Error<InvIssueDto>(ex, "recording a return"); }
        }

        public async Task<ApiResponse<InvIssueDto>> VoidIssueAsync(string? schoolId, long userId, long id, InvVoidRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvIssueDto>();
            var reason = InventoryRules.Clean(req.Reason);
            if (reason == null) return Result<InvIssueDto>(400, "A reason is required to void an issue.", null);
            if (reason.Length > InventoryRules.MaxRemarks) return Result<InvIssueDto>(400, "The reason can be at most 500 characters.", null);
            try
            {
                await using var tx = await Repo.BeginTransactionAsync(ct);
                var issue = await Repo.LockIssueAsync(schoolId, id, ct);
                if (issue == null) return Result<InvIssueDto>(404, "Issue not found.", null);
                if (issue.Status == PaymentStatuses.Voided) return Result<InvIssueDto>(409, "This issue has already been voided.", null);

                var lines = await Repo.GetTrackedLinesAsync(schoolId, id, ct);
                if (lines.Any(l => l.ReturnedQuantity > 0))
                    return Result<InvIssueDto>(409, "This issue can't be voided because some of it has already been returned.", null);

                foreach (var line in lines)
                {
                    var change = await _ledger.ApplyAsync(schoolId, line.ItemId, line.Quantity, InvMovementTypes.IssueVoid, userId,
                        new StockChangeInfo { ReferenceType = InvReferenceTypes.IssueLine, ReferenceId = line.IssueLineId, Remarks = reason }, ct);
                    if (!change.Ok) return Result<InvIssueDto>(500, "Something went wrong. Please try again later.", null);
                }
                issue.Status = PaymentStatuses.Voided; issue.VoidedBy = userId; issue.VoidedAt = DateTime.UtcNow; issue.VoidReason = reason;
                await Repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Issue voided and the stock restored.", (await LoadAsync(schoolId, id, ct))!);
            }
            catch (Exception ex) { return Error<InvIssueDto>(ex, "voiding an issue"); }
        }

        public async Task<ApiResponse<List<InvRecipientDto>>> SearchRecipientsAsync(string? schoolId, string? type, string? q, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<List<InvRecipientDto>>();
            try
            {
                var t = InventoryRules.CleanUpper(type) ?? "";
                if (!InvRecipientTypes.All.Contains(t)) return Result<List<InvRecipientDto>>(400, "Recipient type must be STAFF, STUDENT or DEPARTMENT.", null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<List<InvRecipientDto>>();
                var term = InventoryRules.Clean(q);
                if (term != null && term.Length > 100) return Result<List<InvRecipientDto>>(400, "The search text is too long.", null);
                var rows = await Repo.SearchRecipientsAsync(schoolId, t, term, active.Value, InventoryRules.RecipientSearchCap, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new InvRecipientDto { Id = r.Id, Name = r.Name, Detail = r.Detail }).ToList());
            }
            catch (Exception ex) { return Error<List<InvRecipientDto>>(ex, "searching issue recipients"); }
        }
    }
}
