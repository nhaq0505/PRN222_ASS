using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.BusinessObjects.Entities;

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid PaidByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public decimal SubtotalAmount { get; set; }
    public decimal TaxOrTipAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public ExpenseSplitMode SplitMode { get; set; } = ExpenseSplitMode.Equal;
    public string? ReceiptImageUrl { get; set; }
    public ReceiptScanStatus ScanStatus { get; set; } = ReceiptScanStatus.None;
    public DateTime SpentAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Trip? Trip { get; set; }
    public User? PaidByUser { get; set; }
    public ICollection<ExpenseSplit> Splits { get; set; } = new List<ExpenseSplit>();
    public ICollection<ReceiptItem> ReceiptItems { get; set; } = new List<ReceiptItem>();
}

public partial class Trip
{
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
