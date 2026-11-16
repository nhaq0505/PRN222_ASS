using CourtBook.BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class CourtBookDbContext : DbContext
{
    public CourtBookDbContext(DbContextOptions<CourtBookDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<VenueReview> VenueReviews => Set<VenueReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.FullName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(150);
            entity.Property(u => u.PhoneNumber).IsRequired().HasMaxLength(20);
            entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
            entity.Property(u => u.Role).HasConversion<int>();
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Venue>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Slug).IsRequired().HasMaxLength(120);
            entity.Property(v => v.Name).IsRequired().HasMaxLength(150);
            entity.Property(v => v.District).IsRequired().HasMaxLength(100);
            entity.Property(v => v.Address).IsRequired().HasMaxLength(250);
            entity.Property(v => v.PhoneNumber).IsRequired().HasMaxLength(25);
            entity.Property(v => v.Description).IsRequired().HasMaxLength(600);
            entity.Property(v => v.AccentColor).IsRequired().HasMaxLength(30);
            entity.Property(v => v.CoverImageUrl).IsRequired().HasMaxLength(500);
            entity.Property(v => v.GalleryImageUrls).IsRequired().HasMaxLength(2000);
            entity.Property(v => v.Status).HasConversion<int>();

            entity.HasIndex(v => v.Slug).IsUnique();
            entity.HasIndex(v => new { v.Status, v.District });
            entity.HasIndex(v => v.OwnerId);

            entity.HasOne(v => v.Owner)
                .WithMany(u => u.OwnedVenues)
                .HasForeignKey(v => v.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Court>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).IsRequired().HasMaxLength(20);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
            entity.Property(c => c.SportType).HasConversion<int>();
            entity.Property(c => c.Surface).IsRequired().HasMaxLength(100);
            entity.Property(c => c.ImageUrl).HasMaxLength(500);
            entity.Property(c => c.BasePricePerHour).HasPrecision(18, 0);
            entity.Property(c => c.PeakPricePerHour).HasPrecision(18, 0);

            entity.HasIndex(c => new { c.VenueId, c.Code }).IsUnique();
            entity.HasIndex(c => new { c.VenueId, c.DisplayOrder });

            entity.HasOne(c => c.Venue)
                .WithMany(v => v.Courts)
                .HasForeignKey(c => c.VenueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.BookingCode).IsRequired().HasMaxLength(30);
            entity.Property(b => b.PriceVnd).HasPrecision(18, 0);
            entity.Property(b => b.Status).HasConversion<int>();
            entity.Property(b => b.Notes).HasMaxLength(500);
            entity.Property(b => b.PaymentMethod).IsRequired().HasMaxLength(50);

            entity.HasIndex(b => b.BookingCode).IsUnique();
            entity.HasIndex(b => b.PlayDate);
            entity.HasIndex(b => new { b.UserId, b.Status });

            // Prevent double-booking for active slots (Held=1, Confirmed=2, CheckedIn=3)
            // while preserving Cancelled=4 and Expired=5 history rows.
            var bookingSlotIndex = entity.HasIndex(b => new { b.CourtId, b.PlayDate, b.StartHour })
                .IsUnique();

            if (Database.IsSqlServer())
            {
                bookingSlotIndex.HasFilter("[Status] IN (1, 2, 3)");
            }
            else
            {
                bookingSlotIndex.HasFilter("Status IN (1, 2, 3)");
            }

            entity.HasOne(b => b.Court)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VenueReview>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Comment).IsRequired().HasMaxLength(600);
            entity.Property(r => r.Rating).IsRequired();

            entity.HasIndex(r => r.VenueId);
            entity.HasIndex(r => r.UserId);

            entity.HasOne(r => r.Venue)
                .WithMany(v => v.Reviews)
                .HasForeignKey(r => r.VenueId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
