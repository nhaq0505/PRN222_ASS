namespace SplitTrip.BusinessObjects.Entities;

public class ReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }

    public Expense? Expense { get; set; }
    public ICollection<ReceiptItemClaim> Claims { get; set; } = new List<ReceiptItemClaim>();
}
