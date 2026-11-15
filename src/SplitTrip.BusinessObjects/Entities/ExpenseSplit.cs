namespace SplitTrip.BusinessObjects.Entities;

public class ExpenseSplit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public Guid UserId { get; set; }
    public decimal ShareRatioOrWeight { get; set; }
    public decimal OwedAmount { get; set; }

    public Expense? Expense { get; set; }
    public User? User { get; set; }
}
