using MaskMap.Api;
using MaskMap.Api.Domains;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace MaskMap.AcceptanceTests;

[TestClass]
public sealed class AppDbContextMsSqlContainerTests
{
    private static MsSqlContainer? _sqlServer;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sqlServer.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_sqlServer is not null)
        {
            await _sqlServer.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task AppDbContextCanCreateSchemaAndPersistDomainModelsOnMsSql()
    {
        var connectionString = new SqlConnectionStringBuilder(_sqlServer!.GetConnectionString())
        {
            InitialCatalog = "MaskMapTests"
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new AppDbContext(options);

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Pharmacies.Add(new Pharmacy
        {
            PharmacyId = "pharmacy-001",
            Name = "信義健康藥局",
            Lat = 25.033000m,
            Lng = 121.565400m
        });

        db.Inventories.Add(new Inventory
        {
            PharmacyId = "pharmacy-001",
            ProductId = "mask-adult",
            AvailableQuantity = 10
        });

        db.Reservations.Add(new Reservation
        {
            ReservationId = "reservation-001",
            Status = "Reserved",
            PharmacyId = "pharmacy-001",
            ProductId = "mask-adult",
            Quantity = 3,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        });

        await db.SaveChangesAsync();

        var reservation = await db.Reservations.SingleAsync();
        var inventory = await db.Inventories.SingleAsync();

        Assert.AreEqual("reservation-001", reservation.ReservationId);
        Assert.AreEqual("pharmacy-001", reservation.PharmacyId);
        Assert.AreEqual("mask-adult", reservation.ProductId);
        Assert.AreEqual(3, reservation.Quantity);
        Assert.AreEqual(10, inventory.AvailableQuantity);
    }
}
