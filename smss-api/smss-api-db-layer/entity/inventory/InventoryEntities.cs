using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // Inventory & Stock, Stage 1 (Docs/smss-inventory-stage1.sql). Nothing outside the inventory module references these tables.
    // The tenant guarantee lives in the database (composite foreign keys (id, school_id)); these classes carry no navigation properties on purpose.

    /// <summary>Global unit of measure (pcs, kg, litre, ...).</summary>
    [Table("lut_inv_unit", Schema = "public")]
    public class LutInvUnit
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("unit_id")] public long UnitId { get; set; }
        [Column("unit_name")][StringLength(50)] public string UnitName { get; set; } = null!;
        [Column("unit_code")][StringLength(20)] public string UnitCode { get; set; } = null!;
        [Column("is_active")] public bool IsActive { get; set; }
    }

    [Table("tb_inv_categories", Schema = "public")]
    public class TbInvCategory
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("category_id")] public long CategoryId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("category_name")][StringLength(100)] public string CategoryName { get; set; } = null!;
        [Column("category_code")][StringLength(30)] public string CategoryCode { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_inv_suppliers", Schema = "public")]
    public class TbInvSupplier
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("supplier_id")] public long SupplierId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("supplier_name")][StringLength(150)] public string SupplierName { get; set; } = null!;
        [Column("contact_person")][StringLength(100)] public string? ContactPerson { get; set; }
        [Column("mobile")][StringLength(20)] public string? Mobile { get; set; }
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        [Column("address")] public string? Address { get; set; }
        [Column("gstin")][StringLength(15)] public string? Gstin { get; set; }
        [Column("pan")][StringLength(10)] public string? Pan { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// An item of the school store. <see cref="CurrentQuantity"/> is a cached total: it is written ONLY by IStockLedgerService, in the same transaction as a
    /// <see cref="TbInvStockMovement"/> row. No other code may assign it (the item update endpoint does not accept a quantity).
    /// </summary>
    [Table("tb_inv_items", Schema = "public")]
    public class TbInvItem
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("item_id")] public long ItemId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("item_code")][StringLength(50)] public string ItemCode { get; set; } = null!;
        [Column("barcode")][StringLength(100)] public string? Barcode { get; set; }
        [Column("item_name")][StringLength(150)] public string ItemName { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("category_id")] public long CategoryId { get; set; }
        [Column("unit_id")] public long UnitId { get; set; }
        [Column("item_type")][StringLength(12)] public string ItemType { get; set; } = null!;
        [Column("purchase_price", TypeName = "numeric(12,2)")] public decimal PurchasePrice { get; set; }
        [Column("selling_price", TypeName = "numeric(12,2)")] public decimal? SellingPrice { get; set; }
        [Column("is_saleable")] public bool IsSaleable { get; set; }
        [Column("gst_percent", TypeName = "numeric(5,2)")] public decimal GstPercent { get; set; }
        [Column("min_stock_level", TypeName = "numeric(12,3)")] public decimal MinStockLevel { get; set; }
        [Column("current_quantity", TypeName = "numeric(12,3)")] public decimal CurrentQuantity { get; set; }
        [Column("expiry_date", TypeName = "date")] public DateOnly? ExpiryDate { get; set; }
        /// <summary>Key in the PRIVATE file store, never a URL.</summary>
        [Column("photo_path")][StringLength(500)] public string? PhotoPath { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One immutable ledger row per quantity change. A database trigger rejects UPDATE, DELETE and TRUNCATE.</summary>
    [Table("tb_inv_stock_movements", Schema = "public")]
    public class TbInvStockMovement
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("movement_id")] public long MovementId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("item_id")] public long ItemId { get; set; }
        [Column("movement_type")][StringLength(20)] public string MovementType { get; set; } = null!;
        [Column("quantity_change", TypeName = "numeric(12,3)")] public decimal QuantityChange { get; set; }
        [Column("quantity_after", TypeName = "numeric(12,3)")] public decimal QuantityAfter { get; set; }
        [Column("reason_code")][StringLength(30)] public string? ReasonCode { get; set; }
        [Column("reference_type")][StringLength(30)] public string? ReferenceType { get; set; }
        [Column("reference_id")] public long? ReferenceId { get; set; }
        [Column("unit_cost", TypeName = "numeric(12,2)")] public decimal? UnitCost { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    [Table("tb_inv_stock_ins", Schema = "public")]
    public class TbInvStockIn
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("stock_in_id")] public long StockInId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("item_id")] public long ItemId { get; set; }
        [Column("supplier_id")] public long? SupplierId { get; set; }
        [Column("quantity", TypeName = "numeric(12,3)")] public decimal Quantity { get; set; }
        [Column("unit_cost", TypeName = "numeric(12,2)")] public decimal UnitCost { get; set; }
        [Column("received_date", TypeName = "date")] public DateOnly ReceivedDate { get; set; }
        [Column("bill_number")][StringLength(50)] public string? BillNumber { get; set; }
        [Column("expiry_date", TypeName = "date")] public DateOnly? ExpiryDate { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("received_by")] public long ReceivedBy { get; set; }
        /// <summary>"Active" or "Voided" (<see cref="smss_api_service_layer.helper.PaymentStatuses"/> uses the same two words).</summary>
        [Column("status")][StringLength(10)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    [Table("tb_inv_issues", Schema = "public")]
    public class TbInvIssue
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("issue_id")] public long IssueId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("issue_number")][StringLength(50)] public string IssueNumber { get; set; } = null!;
        /// <summary>STAFF, STUDENT or DEPARTMENT; exactly the matching one of employee / student / department is set (a CHECK enforces it).</summary>
        [Column("recipient_type")][StringLength(12)] public string RecipientType { get; set; } = null!;
        [Column("employee_id")][StringLength(20)] public string? EmployeeId { get; set; }
        [Column("student_id")][StringLength(20)] public string? StudentId { get; set; }
        [Column("department_id")] public long? DepartmentId { get; set; }
        [Column("issue_date", TypeName = "date")] public DateOnly IssueDate { get; set; }
        [Column("purpose")] public string? Purpose { get; set; }
        [Column("issued_by")] public long IssuedBy { get; set; }
        [Column("status")][StringLength(10)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    [Table("tb_inv_issue_lines", Schema = "public")]
    public class TbInvIssueLine
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("issue_line_id")] public long IssueLineId { get; set; }
        [Column("issue_id")] public long IssueId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("item_id")] public long ItemId { get; set; }
        [Column("quantity", TypeName = "numeric(12,3)")] public decimal Quantity { get; set; }
        [Column("returned_quantity", TypeName = "numeric(12,3)")] public decimal ReturnedQuantity { get; set; }
    }

    [Table("tb_inv_issue_returns", Schema = "public")]
    public class TbInvIssueReturn
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("return_id")] public long ReturnId { get; set; }
        [Column("issue_line_id")] public long IssueLineId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("quantity", TypeName = "numeric(12,3)")] public decimal Quantity { get; set; }
        [Column("return_date", TypeName = "date")] public DateOnly ReturnDate { get; set; }
        /// <summary>GOOD (stock goes back) or DAMAGED (recorded, stock does not go back).</summary>
        [Column("condition")][StringLength(10)] public string Condition { get; set; } = null!;
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("received_by")] public long ReceivedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
