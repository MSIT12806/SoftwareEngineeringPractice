using Microsoft.EntityFrameworkCore;
using MaskMap.Api.Domains;

namespace MaskMap.Api
{
    public interface IDb
    {
        DbSet<Pharmacy> Pharmacies { get; }
        DbSet<Inventory> Inventories { get; }
        DbSet<Reservation> Reservations { get; }
    }

    public class AppDbContext : DbContext, IDb
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<Reservation> Reservations => Set<Reservation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Pharmacy>(entity =>
            {
                entity.ToTable("Pharmacies");

                entity.HasKey(pharmacy => pharmacy.PharmacyId);

                entity.Property(pharmacy => pharmacy.PharmacyId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(pharmacy => pharmacy.Name)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(pharmacy => pharmacy.Lat)
                    .HasPrecision(9, 6)
                    .IsRequired();

                entity.Property(pharmacy => pharmacy.Lng)
                    .HasPrecision(9, 6)
                    .IsRequired();

                entity.HasIndex(pharmacy => new { pharmacy.Lat, pharmacy.Lng });
            });

            modelBuilder.Entity<Inventory>(entity =>
            {
                entity.ToTable("Inventories", table => table.HasCheckConstraint(
                    "CK_Inventories_AvailableQuantity_NonNegative",
                    "[AvailableQuantity] >= 0"));

                entity.HasKey(inventory => new { inventory.PharmacyId, inventory.ProductId });

                entity.Property(inventory => inventory.PharmacyId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(inventory => inventory.ProductId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(inventory => inventory.AvailableQuantity)
                    .IsRequired();

                entity.HasOne<Pharmacy>()
                    .WithMany()
                    .HasForeignKey(inventory => inventory.PharmacyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Reservation>(entity =>
            {
                entity.ToTable("Reservations", table => table.HasCheckConstraint(
                    "CK_Reservations_Quantity_Positive",
                    "[Quantity] > 0"));

                entity.HasKey(reservation => reservation.ReservationId);

                entity.Property(reservation => reservation.ReservationId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(reservation => reservation.Status)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(reservation => reservation.PharmacyId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(reservation => reservation.ProductId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(reservation => reservation.Quantity)
                    .IsRequired();

                entity.Property(reservation => reservation.ExpiresAt)
                    .IsRequired();

                entity.HasOne<Pharmacy>()
                    .WithMany()
                    .HasForeignKey(reservation => reservation.PharmacyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Inventory>()
                    .WithMany()
                    .HasForeignKey(reservation => new { reservation.PharmacyId, reservation.ProductId })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(reservation => new
                {
                    reservation.PharmacyId,
                    reservation.ProductId,
                    reservation.Status,
                    reservation.ExpiresAt
                });
            });
        }
    }
}
