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
    }
}
