using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MaskMap.AcceptanceTests.InitialRequirements;

[TestClass]
[TestCategory("InitialRequirements")]
public sealed class InitialRequirementsAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient _client = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        var baseUrl = Environment.GetEnvironmentVariable("MASKMAP_API_BASE_URL")
            ?? "http://localhost:5000";

        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        await ResetTestDataAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
    }

    [TestMethod]
    public async Task IR001_UserCanQueryNearbyPharmacyInventories()
    {
        var response = await _client.GetAsync("/api/pharmacies?lat=25.0330&lng=121.5654&radiusKm=3");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync<NearbyPharmaciesResponse>(response);
        var pharmacy = body.Items.SingleOrDefault(x => x.PharmacyId == "pharmacy-001");

        Assert.IsNotNull(pharmacy, "pharmacy-001 should be returned for the nearby pharmacy query.");
        Assert.AreEqual("信義健康藥局", pharmacy.Name);
        Assert.IsTrue(pharmacy.DistanceKm <= 3);

        var inventory = pharmacy.Inventories.SingleOrDefault(x => x.ProductId == "mask-adult");
        Assert.IsNotNull(inventory, "Nearby pharmacy response should include mask-adult inventory.");
        Assert.IsTrue(inventory.AvailableQuantity >= 0);
    }

    [TestMethod]
    public async Task IR002_UserCanCreateReservationAndOccupyInventoryAndQuota()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationRequest(
                "pharmacy-001",
                "mask-adult",
                3))
        };
        request.Headers.Add("X-Test-User-Id", "user-001");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var reservation = await ReadJsonAsync<CreateReservationResponse>(response);
        Assert.AreEqual("Reserved", reservation.Status);
        Assert.AreEqual("pharmacy-001", reservation.PharmacyId);
        Assert.AreEqual("mask-adult", reservation.ProductId);
        Assert.AreEqual(3, reservation.Quantity);
        Assert.IsTrue(reservation.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        Assert.IsTrue(reservation.ExpiresAt <= DateTimeOffset.UtcNow.AddHours(25));

        var afterInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        Assert.AreEqual(beforeInventory.AvailableQuantity - 3, afterInventory.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity + 3, afterInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR003_ReservationFailsWhenInventoryIsInsufficientAndDoesNotOccupyQuota()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-002", "mask-adult");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationRequest(
                "pharmacy-002",
                "mask-adult",
                1))
        };
        request.Headers.Add("X-Test-User-Id", "user-001");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);

        var error = await ReadJsonAsync<ErrorResponse>(response);
        Assert.AreEqual("InventoryInsufficient", error.Code);

        var afterInventory = await GetInventoryAsync("pharmacy-002", "mask-adult");
        Assert.AreEqual(beforeInventory.AvailableQuantity, afterInventory.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity, afterInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR004_ReservationFailsWhenQuotaIsInsufficientAndDoesNotOccupyInventory()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationRequest(
                "pharmacy-001",
                "mask-adult",
                1))
        };
        request.Headers.Add("X-Test-User-Id", "user-no-quota");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);

        var error = await ReadJsonAsync<ErrorResponse>(response);
        Assert.AreEqual("QuotaExceeded", error.Code);

        var afterInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        Assert.AreEqual(beforeInventory.AvailableQuantity, afterInventory.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity, afterInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR005_RetryingSameReservationOperationDoesNotCreateDuplicateReservation()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = new CreateReservationRequest(
            "pharmacy-001",
            "mask-adult",
            1);

        var firstResponse = await SendCreateReservationAsync(
            "user-001",
            idempotencyKey,
            body);

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        var firstReservation = await ReadJsonAsync<CreateReservationResponse>(firstResponse);

        var secondResponse = await SendCreateReservationAsync(
            "user-001",
            idempotencyKey,
            body);

        Assert.IsTrue(
            secondResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Retrying the same operation should return the previous result, not {secondResponse.StatusCode}.");

        var secondReservation = await ReadJsonAsync<CreateReservationResponse>(secondResponse);
        Assert.AreEqual(firstReservation.ReservationId, secondReservation.ReservationId);

        var afterInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        Assert.AreEqual(beforeInventory.AvailableQuantity - 1, afterInventory.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity + 1, afterInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR006_SameOperationIdentifierCannotBeReusedWithDifferentRequestBody()
    {
        var beforePharmacy001Inventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        var beforePharmacy002Inventory = await GetInventoryAsync("pharmacy-002", "mask-adult");
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstResponse = await SendCreateReservationAsync(
            "user-001",
            idempotencyKey,
            new CreateReservationRequest(
                "pharmacy-001",
                "mask-adult",
                1));

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await SendCreateReservationAsync(
            "user-001",
            idempotencyKey,
            new CreateReservationRequest(
                "pharmacy-002",
                "mask-adult",
                1));

        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var error = await ReadJsonAsync<ErrorResponse>(secondResponse);
        Assert.AreEqual("IdempotencyKeyConflict", error.Code);

        var afterPharmacy001Inventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        var afterPharmacy002Inventory = await GetInventoryAsync("pharmacy-002", "mask-adult");

        Assert.AreEqual(beforePharmacy001Inventory.AvailableQuantity - 1, afterPharmacy001Inventory.AvailableQuantity);
        Assert.AreEqual(beforePharmacy001Inventory.ReservedQuantity + 1, afterPharmacy001Inventory.ReservedQuantity);
        Assert.AreEqual(beforePharmacy002Inventory.AvailableQuantity, afterPharmacy002Inventory.AvailableQuantity);
        Assert.AreEqual(beforePharmacy002Inventory.ReservedQuantity, afterPharmacy002Inventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR007_UserCanQueryOnlyOwnReservations()
    {
        var user1Reservation = await CreateReservationAsync(
            "user-001",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 1));
        var user2Reservation = await CreateReservationAsync(
            "user-002",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 1));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/reservations");
        request.Headers.Add("X-Test-User-Id", "user-001");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync<UserReservationsResponse>(response);
        Assert.IsTrue(body.Items.Any(x => x.ReservationId == user1Reservation.ReservationId));
        Assert.IsFalse(body.Items.Any(x => x.ReservationId == user2Reservation.ReservationId));
    }

    [TestMethod]
    public async Task IR008_PharmacyCanReportInventoryReceipt()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pharmacy/inventory/receipts")
        {
            Content = JsonContent.Create(new InventoryReceiptRequest(
                "mask-adult",
                100,
                DateTimeOffset.UtcNow), options: JsonOptions)
        };
        request.Headers.Add("X-Test-Pharmacy-Id", "pharmacy-001");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var receiptResult = await ReadJsonAsync<PharmacyInventoryStateResponse>(response);
        Assert.AreEqual(beforeInventory.PhysicalQuantity + 100, receiptResult.PhysicalQuantity);
        Assert.AreEqual(beforeInventory.AvailableQuantity + 100, receiptResult.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity, receiptResult.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR009_PharmacyCanCollectReservation()
    {
        var reservation = await CreateReservationAsync(
            "user-001",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 3));
        var beforeCollectInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pharmacy/reservations/{reservation.ReservationId}/collect")
        {
            Content = JsonContent.Create(new CollectReservationRequest(
                "user-001",
                DateTimeOffset.UtcNow), options: JsonOptions)
        };
        request.Headers.Add("X-Test-Pharmacy-Id", "pharmacy-001");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var collectResult = await ReadJsonAsync<CollectReservationResponse>(response);
        Assert.AreEqual(reservation.ReservationId, collectResult.ReservationId);
        Assert.AreEqual("Collected", collectResult.Status);

        var afterCollectInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        Assert.AreEqual(beforeCollectInventory.AvailableQuantity, afterCollectInventory.AvailableQuantity);
        Assert.AreEqual(beforeCollectInventory.ReservedQuantity - 3, afterCollectInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task IR010_PharmacyCanAdjustInventory()
    {
        var beforeInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pharmacy/inventory/adjustments")
        {
            Content = JsonContent.Create(new InventoryAdjustmentRequest(
                "mask-adult",
                -2,
                "Damaged",
                DateTimeOffset.UtcNow), options: JsonOptions)
        };
        request.Headers.Add("X-Test-Pharmacy-Id", "pharmacy-001");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var adjustmentResult = await ReadJsonAsync<PharmacyInventoryStateResponse>(response);
        Assert.AreEqual(beforeInventory.PhysicalQuantity - 2, adjustmentResult.PhysicalQuantity);
        Assert.AreEqual(beforeInventory.AvailableQuantity - 2, adjustmentResult.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity, adjustmentResult.ReservedQuantity);
        Assert.IsTrue(adjustmentResult.PhysicalQuantity >= 0);
        Assert.IsTrue(adjustmentResult.AvailableQuantity >= 0);
    }

    [TestMethod]
    public async Task IR011_PharmacyCanQueryOnlyItsActiveReservations()
    {
        var pharmacy001Reserved = await CreateReservationAsync(
            "user-001",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 1));
        var pharmacy001Collected = await CreateReservationAsync(
            "user-002",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 1));
        await CollectReservationAsync("pharmacy-001", pharmacy001Collected.ReservationId, "user-002");

        await ReportInventoryReceiptAsync("pharmacy-002", "mask-adult", 1);
        var pharmacy002Reserved = await CreateReservationAsync(
            "user-002",
            new CreateReservationRequest("pharmacy-002", "mask-adult", 1));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/pharmacy/reservations?status=Reserved");
        request.Headers.Add("X-Test-Pharmacy-Id", "pharmacy-001");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync<PharmacyReservationsResponse>(response);
        Assert.IsTrue(body.Items.Any(x => x.ReservationId == pharmacy001Reserved.ReservationId));
        Assert.IsFalse(body.Items.Any(x => x.ReservationId == pharmacy001Collected.ReservationId));
        Assert.IsFalse(body.Items.Any(x => x.ReservationId == pharmacy002Reserved.ReservationId));
        Assert.IsTrue(body.Items.All(x => x.Status == "Reserved"));
    }

    [TestMethod]
    public async Task IR012_ExpiredReservationIsReleasedByBackgroundJob()
    {
        var reservation = await CreateReservationAsync(
            "user-001",
            new CreateReservationRequest("pharmacy-001", "mask-adult", 3));
        var beforeReleaseInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");

        await SetTestClockAsync(reservation.ExpiresAt.AddSeconds(1));

        var response = await _client.PostAsync("/api/test/jobs/release-expired-reservations", content: null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var releaseResult = await ReadJsonAsync<ReleaseExpiredReservationsResponse>(response);
        Assert.IsTrue(releaseResult.Released >= 1);

        var afterReleaseInventory = await GetInventoryAsync("pharmacy-001", "mask-adult");
        Assert.AreEqual(beforeReleaseInventory.AvailableQuantity + 3, afterReleaseInventory.AvailableQuantity);
        Assert.AreEqual(beforeReleaseInventory.ReservedQuantity - 3, afterReleaseInventory.ReservedQuantity);

        var userReservationsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me/reservations");
        userReservationsRequest.Headers.Add("X-Test-User-Id", "user-001");
        var userReservationsResponse = await _client.SendAsync(userReservationsRequest);
        Assert.AreEqual(HttpStatusCode.OK, userReservationsResponse.StatusCode);

        var userReservations = await ReadJsonAsync<UserReservationsResponse>(userReservationsResponse);
        var expiredReservation = userReservations.Items.SingleOrDefault(x => x.ReservationId == reservation.ReservationId);
        Assert.IsNotNull(expiredReservation);
        Assert.AreEqual("Expired", expiredReservation.Status);
    }

    private async Task<PharmacyInventoryDto> GetInventoryAsync(string pharmacyId, string productId)
    {
        var response = await _client.GetAsync($"/api/pharmacies/{pharmacyId}/inventories");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync<PharmacyInventoriesResponse>(response);
        var inventory = body.Items.SingleOrDefault(x => x.ProductId == productId);

        Assert.IsNotNull(inventory, $"{pharmacyId}/{productId} inventory should exist.");
        return inventory;
    }

    private async Task<HttpResponseMessage> SendCreateReservationAsync(
        string userId,
        string idempotencyKey,
        CreateReservationRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Test-User-Id", userId);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await _client.SendAsync(request);
    }

    private async Task<CreateReservationResponse> CreateReservationAsync(
        string userId,
        CreateReservationRequest body)
    {
        var response = await SendCreateReservationAsync(
            userId,
            Guid.NewGuid().ToString(),
            body);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync<CreateReservationResponse>(response);
    }

    private async Task CollectReservationAsync(
        string pharmacyId,
        string reservationId,
        string userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pharmacy/reservations/{reservationId}/collect")
        {
            Content = JsonContent.Create(new CollectReservationRequest(
                userId,
                DateTimeOffset.UtcNow), options: JsonOptions)
        };
        request.Headers.Add("X-Test-Pharmacy-Id", pharmacyId);

        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task ReportInventoryReceiptAsync(
        string pharmacyId,
        string productId,
        int quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pharmacy/inventory/receipts")
        {
            Content = JsonContent.Create(new InventoryReceiptRequest(
                productId,
                quantity,
                DateTimeOffset.UtcNow), options: JsonOptions)
        };
        request.Headers.Add("X-Test-Pharmacy-Id", pharmacyId);

        var response = await _client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task ResetTestDataAsync()
    {
        var response = await _client.PostAsync("/api/test/reset", content: null);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            "Acceptance tests require a test-only POST /api/test/reset endpoint so each test starts from known seed data.");
    }

    private async Task SetTestClockAsync(DateTimeOffset utcNow)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/test/clock",
            new SetClockRequest(utcNow),
            JsonOptions);

        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            "IR-012 requires a test-only POST /api/test/clock endpoint to avoid waiting 24 hours.");
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        Assert.IsNotNull(value, $"Response body should be valid {typeof(T).Name} JSON.");
        return value;
    }
}

public sealed record NearbyPharmaciesResponse(
    IReadOnlyList<NearbyPharmacyDto> Items);

public sealed record NearbyPharmacyDto(
    string PharmacyId,
    string Name,
    string Address,
    double DistanceKm,
    IReadOnlyList<PharmacyInventorySummaryDto> Inventories);

public sealed record PharmacyInventorySummaryDto(
    string ProductId,
    string ProductName,
    int AvailableQuantity,
    DateTimeOffset LastUpdatedAt);

public sealed record PharmacyInventoriesResponse(
    string PharmacyId,
    IReadOnlyList<PharmacyInventoryDto> Items);

public sealed record PharmacyInventoryDto(
    string ProductId,
    int AvailableQuantity,
    int ReservedQuantity,
    int PhysicalQuantity,
    DateTimeOffset LastUpdatedAt);

public sealed record CreateReservationRequest(
    string PharmacyId,
    string ProductId,
    int Quantity);

public sealed record CreateReservationResponse(
    string ReservationId,
    string Status,
    string PharmacyId,
    string ProductId,
    int Quantity,
    DateTimeOffset ExpiresAt);

public sealed record ErrorResponse(
    string Code,
    string Message);

public sealed record UserReservationsResponse(
    IReadOnlyList<UserReservationDto> Items);

public sealed record UserReservationDto(
    string ReservationId,
    string Status,
    string PharmacyId,
    string ProductId,
    int Quantity,
    DateTimeOffset ExpiresAt);

public sealed record InventoryReceiptRequest(
    string ProductId,
    int Quantity,
    DateTimeOffset OccurredAt);

public sealed record InventoryAdjustmentRequest(
    string ProductId,
    int DeltaQuantity,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record PharmacyInventoryStateResponse(
    string PharmacyId,
    string ProductId,
    int PhysicalQuantity,
    int AvailableQuantity,
    int ReservedQuantity);

public sealed record CollectReservationRequest(
    string UserId,
    DateTimeOffset CollectedAt);

public sealed record CollectReservationResponse(
    string ReservationId,
    string Status,
    DateTimeOffset CollectedAt);

public sealed record PharmacyReservationsResponse(
    IReadOnlyList<PharmacyReservationDto> Items);

public sealed record PharmacyReservationDto(
    string ReservationId,
    string UserId,
    string ProductId,
    int Quantity,
    string Status,
    DateTimeOffset ExpiresAt);

public sealed record ReleaseExpiredReservationsResponse(
    int Scanned,
    int Released,
    int Skipped);

public sealed record SetClockRequest(
    DateTimeOffset UtcNow);
