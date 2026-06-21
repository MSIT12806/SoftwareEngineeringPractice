using MaskMap.Api.Domains;
using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Infrastructure.Reservations;

public sealed class UpdlockReservationCapacityClaimer
    : IReservationCapacityClaimer
{
    private readonly AppDbContext _db;

    public UpdlockReservationCapacityClaimer(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReservationCapacityClaimResult> TryClaimAsync(
        string userId,
        string pharmacyId,
        string productId,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var quota = await _db.UserQuotas
            .FromSqlInterpolated($$"""
                SELECT [UserId], [Limit], [ReservedQuantity], [PurchasedQuantity],
                       [RowVersion]
                FROM [UserQuotas] WITH (UPDLOCK)
                WHERE [UserId] = {{userId}}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (quota is null ||
            quota.Limit - quota.ReservedQuantity - quota.PurchasedQuantity < quantity)
        {
            return ReservationCapacityClaimResult.QuotaExceeded;
        }

        var inventory = await _db.Inventories
            .FromSqlInterpolated($$"""
                SELECT [PharmacyId], [ProductId], [AvailableQuantity],
                       [ReservedQuantity], [PhysicalQuantity], [LastUpdatedAt],
                       [RowVersion]
                FROM [Inventories] WITH (UPDLOCK)
                WHERE [PharmacyId] = {{pharmacyId}}
                  AND [ProductId] = {{productId}}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (inventory is null || inventory.AvailableQuantity < quantity)
        {
            return ReservationCapacityClaimResult.InventoryInsufficient;
        }

        quota.ReservedQuantity += quantity;
        inventory.AvailableQuantity -= quantity;
        inventory.ReservedQuantity += quantity;
        inventory.LastUpdatedAt = occurredAt;

        await _db.SaveChangesAsync(cancellationToken);

        return ReservationCapacityClaimResult.Succeeded;
    }
}
