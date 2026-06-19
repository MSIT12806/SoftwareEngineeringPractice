using System.Net;
using System.Net.Http.Json;
using NBomber.CSharp;

const int defaultContenderCount = 10_000;
const int defaultStock = 100;
const int defaultRequestsPerSecond = 1_000;

var baseUrl = Environment.GetEnvironmentVariable("MASKMAP_API_BASE_URL")
    ?? "http://localhost:5000";
var contenderCount = ReadPositiveInt("MASKMAP_CONTENDER_COUNT", defaultContenderCount);
var stock = ReadPositiveInt("MASKMAP_STOCK", defaultStock);
var requestsPerSecond = ReadPositiveInt(
    "MASKMAP_REQUESTS_PER_SECOND",
    defaultRequestsPerSecond);

if (stock > contenderCount)
{
    throw new InvalidOperationException("MASKMAP_STOCK cannot exceed MASKMAP_CONTENDER_COUNT.");
}

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30)
};

var prepareResponse = await httpClient.PostAsJsonAsync(
    "/api/test/last-inventory-competition/prepare",
    new CompetitionPreparation(contenderCount, stock));
await EnsureSuccessAsync(prepareResponse, "prepare competition data");

var issuedRequests = 0;
var created = 0;
var inventoryInsufficient = 0;
var unexpected = 0;

var scenario = Scenario.Create("last_inventory_competition", async _ =>
    {
        var contenderNumber = Interlocked.Increment(ref issuedRequests);
        if (contenderNumber > contenderCount)
        {
            return Response.Ok(statusCode: "not-issued");
        }

        var userId = $"competition-user-{contenderNumber:D6}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new ReservationRequest(
                "competition-pharmacy",
                "mask-adult",
                1))
        };
        request.Headers.Add("X-Test-User-Id", userId);
        request.Headers.Add("Idempotency-Key", $"competition-{contenderNumber:D6}");

        try
        {
            using var response = await httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                Interlocked.Increment(ref created);
                return Response.Ok(statusCode: "201");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>();
                if (error?.Code == "InventoryInsufficient")
                {
                    Interlocked.Increment(ref inventoryInsufficient);
                    return Response.Ok(statusCode: "409 InventoryInsufficient");
                }
            }

            Interlocked.Increment(ref unexpected);
            var body = await response.Content.ReadAsStringAsync();
            return Response.Fail(
                message: $"Unexpected {(int)response.StatusCode}: {body}",
                statusCode: ((int)response.StatusCode).ToString());
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref unexpected);
            return Response.Fail(message: exception.Message);
        }
    })
    .WithoutWarmUp()
    .WithLoadSimulations(
        Simulation.Inject(
            rate: requestsPerSecond,
            interval: TimeSpan.FromSeconds(1),
            during: TimeSpan.FromSeconds(
                Math.Ceiling((double)contenderCount / requestsPerSecond))));

NBomberRunner
    .RegisterScenarios(scenario)
    .WithTestSuite("MaskMap")
    .WithTestName("Last inventory competition")
    .Run();

var finalState = await httpClient.GetFromJsonAsync<CompetitionState>(
    "/api/test/last-inventory-competition/state")
    ?? throw new InvalidOperationException("Competition state response was empty.");

var failures = new List<string>();
Expect(created, stock, "HTTP 201 count", failures);
Expect(inventoryInsufficient, contenderCount - stock, "InventoryInsufficient count", failures);
Expect(unexpected, 0, "unexpected response count", failures);
Expect(finalState.PhysicalQuantity, stock, "physical inventory", failures);
Expect(finalState.AvailableQuantity, 0, "available inventory", failures);
Expect(finalState.ReservedQuantity, stock, "reserved inventory", failures);
Expect(finalState.ReservationCount, stock, "reservation count", failures);
Expect(finalState.OccupiedQuota, stock, "occupied quota", failures);

if (failures.Count > 0)
{
    throw new InvalidOperationException(
        "Last inventory competition invariants failed:" +
        Environment.NewLine +
        string.Join(Environment.NewLine, failures.Select(message => $"- {message}")));
}

Console.WriteLine(
    $"PASS: {contenderCount} contenders competed for {stock} items; " +
    $"exactly {created} reservations succeeded and no inventory was oversold.");

static int ReadPositiveInt(string name, int defaultValue)
{
    var rawValue = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return defaultValue;
    }

    if (!int.TryParse(rawValue, out var value) || value <= 0)
    {
        throw new InvalidOperationException($"{name} must be a positive integer.");
    }

    return value;
}

static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
{
    if (response.IsSuccessStatusCode)
    {
        return;
    }

    var body = await response.Content.ReadAsStringAsync();
    throw new InvalidOperationException(
        $"Failed to {operation}: HTTP {(int)response.StatusCode} {body}");
}

static void Expect(int actual, int expected, string name, ICollection<string> failures)
{
    if (actual != expected)
    {
        failures.Add($"{name}: expected {expected}, actual {actual}");
    }
}

internal sealed record CompetitionPreparation(int ContenderCount, int Stock);
internal sealed record ReservationRequest(string PharmacyId, string ProductId, int Quantity);
internal sealed record ApiError(string Code, string Message);
internal sealed record CompetitionState(
    int PhysicalQuantity,
    int AvailableQuantity,
    int ReservedQuantity,
    int ReservationCount,
    int OccupiedQuota);
