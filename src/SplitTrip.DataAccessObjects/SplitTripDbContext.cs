using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.DataAccessObjects;

public class SplitTripDbContext(DbContextOptions<SplitTripDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripMember> TripMembers => Set<TripMember>();
    public DbSet<TripDay> TripDays => Set<TripDay>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();
    public DbSet<ItineraryVote> ItineraryVotes => Set<ItineraryVote>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();
    public DbSet<ReceiptItemClaim> ReceiptItemClaims => Set<ReceiptItemClaim>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).IsRequired().HasMaxLength(256);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).IsRequired().HasMaxLength(256);
            entity.Property(x => x.FullName).IsRequired().HasMaxLength(150);
            entity.Property(x => x.AvatarUrl).HasMaxLength(500);
            entity.Property(x => x.BankCode).HasMaxLength(32);
            entity.Property(x => x.BankBin).HasMaxLength(16);
            entity.Property(x => x.BankAccountNumber).HasMaxLength(50);
            entity.Property(x => x.BankAccountHolder).HasMaxLength(150);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Destination).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.InviteCode).IsRequired().HasMaxLength(16);
            entity.HasIndex(x => x.InviteCode).IsUnique();
            entity.Property(x => x.EstimatedBudgetPerPerson).HasColumnType("decimal(18,0)");

            entity.HasOne(x => x.OwnerUser)
                .WithMany(u => u.OwnedTrips)
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripMember>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TripId, x.UserId }).IsUnique();

            entity.HasOne(x => x.Trip)
                .WithMany(t => t.Members)
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany(u => u.TripMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripDay>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TripId, x.DayNumber }).IsUnique();

            entity.HasOne(x => x.Trip)
                .WithMany(t => t.Days)
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItineraryItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
            entity.Property(x => x.LocationName).HasMaxLength(250);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.EstimatedCost).HasColumnType("decimal(18,0)");

            entity.HasOne(x => x.TripDay)
                .WithMany(d => d.Items)
                .HasForeignKey(x => x.TripDayId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItineraryVote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ItineraryItemId, x.UserId }).IsUnique();

            entity.HasOne(x => x.ItineraryItem)
                .WithMany(i => i.Votes)
                .HasForeignKey(x => x.ItineraryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
            entity.Property(x => x.ReceiptImageUrl).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.SubtotalAmount).HasColumnType("decimal(18,0)");
            entity.Property(x => x.TaxOrTipAmount).HasColumnType("decimal(18,0)");
            entity.Property(x => x.DiscountAmount).HasColumnType("decimal(18,0)");
            entity.Property(x => x.TotalAmount).HasColumnType("decimal(18,0)");

            entity.HasOne(x => x.Trip)
                .WithMany(t => t.Expenses)
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.PaidByUser)
                .WithMany()
                .HasForeignKey(x => x.PaidByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseSplit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShareRatioOrWeight).HasColumnType("decimal(18,4)");
            entity.Property(x => x.OwedAmount).HasColumnType("decimal(18,0)");
            entity.HasIndex(x => new { x.ExpenseId, x.UserId }).IsUnique();

            entity.HasOne(x => x.Expense)
                .WithMany(e => e.Splits)
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReceiptItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemName).IsRequired().HasMaxLength(250);
            entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,0)");
            entity.Property(x => x.LineTotal).HasColumnType("decimal(18,0)");

            entity.HasOne(x => x.Expense)
                .WithMany(e => e.ReceiptItems)
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReceiptItemClaim>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ReceiptItemId, x.UserId }).IsUnique();

            entity.HasOne(x => x.ReceiptItem)
                .WithMany(r => r.Claims)
                .HasForeignKey(x => x.ReceiptItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Settlement>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            entity.Property(x => x.TransferNote).IsRequired().HasMaxLength(200);
            entity.Property(x => x.ProofNote).HasMaxLength(1000);
            entity.Property(x => x.DisputeReason).HasMaxLength(1000);

            entity.HasOne(x => x.Trip)
                .WithMany(t => t.Settlements)
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.FromUser)
                .WithMany()
                .HasForeignKey(x => x.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ToUser)
                .WithMany()
                .HasForeignKey(x => x.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Message).IsRequired().HasMaxLength(1000);
            entity.Property(x => x.ActionUrl).HasMaxLength(500);
            entity.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });

            entity.HasOne(x => x.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Trip)
                .WithMany(t => t.Notifications)
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
