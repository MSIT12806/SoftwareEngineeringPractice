using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/test")]
    public sealed class TestDataController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _environment;

        public TestDataController(AppDbContext db, IWebHostEnvironment environment)
        {
            _db = db;
            _environment = environment;
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset(CancellationToken cancellationToken)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            await _db.Database.EnsureDeletedAsync(cancellationToken);
            await _db.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;

            _db.Pharmacies.AddRange(
                new Pharmacy
                {
                    PharmacyId = "pharmacy-001",
                    Name = "信義健康藥局",
                    Lat = 25.033000m,
                    Lng = 121.565400m
                },
                new Pharmacy
                {
                    PharmacyId = "pharmacy-002",
                    Name = "測試藥局",
                    Lat = 25.043000m,
                    Lng = 121.575400m
                });

            _db.Inventories.AddRange(
                new Inventory
                {
                    PharmacyId = "pharmacy-001",
                    ProductId = "mask-adult",
                    AvailableQuantity = 10,
                    ReservedQuantity = 0,
                    PhysicalQuantity = 10,
                    LastUpdatedAt = now
                },
                new Inventory
                {
                    PharmacyId = "pharmacy-002",
                    ProductId = "mask-adult",
                    AvailableQuantity = 0,
                    ReservedQuantity = 0,
                    PhysicalQuantity = 0,
                    LastUpdatedAt = now
                });

            _db.UserQuotas.AddRange(
                new UserQuota
                {
                    UserId = "user-001",
                    Limit = 3,
                    ReservedQuantity = 0,
                    PurchasedQuantity = 0
                },
                new UserQuota
                {
                    UserId = "user-002",
                    Limit = 3,
                    ReservedQuantity = 0,
                    PurchasedQuantity = 0
                },
                new UserQuota
                {
                    UserId = "user-no-quota",
                    Limit = 0,
                    ReservedQuantity = 0,
                    PurchasedQuantity = 0
                });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok();
        }

        [HttpPost("last-inventory-competition/prepare")]
        public async Task<IActionResult> PrepareLastInventoryCompetition(
            [FromBody] LastInventoryCompetitionPreparation request,
            CancellationToken cancellationToken)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            if (request.ContenderCount is < 1 or > 100_000 ||
                request.Stock is < 1 ||
                request.Stock > request.ContenderCount)
            {
                return BadRequest(new
                {
                    Message = "ContenderCount must be 1..100000 and Stock must be 1..ContenderCount."
                });
            }

            await _db.Database.EnsureDeletedAsync(cancellationToken);
            await _db.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            _db.Pharmacies.Add(new Pharmacy
            {
                PharmacyId = "competition-pharmacy",
                Name = "最後庫存競爭測試藥局",
                Lat = 25.033000m,
                Lng = 121.565400m
            });
            _db.Inventories.Add(new Inventory
            {
                PharmacyId = "competition-pharmacy",
                ProductId = "mask-adult",
                AvailableQuantity = request.Stock,
                ReservedQuantity = 0,
                PhysicalQuantity = request.Stock,
                LastUpdatedAt = now
            });

            _db.UserQuotas.AddRange(
                Enumerable.Range(1, request.ContenderCount).Select(index => new UserQuota
                {
                    UserId = $"competition-user-{index:D6}",
                    Limit = 1,
                    ReservedQuantity = 0,
                    PurchasedQuantity = 0
                }));

            await _db.SaveChangesAsync(cancellationToken);
            return Ok();
        }

        [HttpGet("last-inventory-competition/state")]
        public async Task<IActionResult> GetLastInventoryCompetitionState(
            CancellationToken cancellationToken)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            var inventory = await _db.Inventories
                .AsNoTracking()
                .SingleAsync(
                    item => item.PharmacyId == "competition-pharmacy" &&
                            item.ProductId == "mask-adult",
                    cancellationToken);

            var reservationCount = await _db.Reservations.CountAsync(cancellationToken);
            var occupiedQuota = await _db.UserQuotas
                .SumAsync(item => item.ReservedQuantity, cancellationToken);

            return Ok(new
            {
                inventory.PhysicalQuantity,
                inventory.AvailableQuantity,
                inventory.ReservedQuantity,
                ReservationCount = reservationCount,
                OccupiedQuota = occupiedQuota
            });
        }
    }

    public sealed record LastInventoryCompetitionPreparation(int ContenderCount, int Stock);
}
