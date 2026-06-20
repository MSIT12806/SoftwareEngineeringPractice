using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using NBomber.CSharp;
using Testcontainers.MsSql;

const int defaultContenderCount = 10_000;
const int defaultStock = 100;
const int defaultRequestsPerSecond = 1_000;

var contenderCount = ReadPositiveInt("MASKMAP_CONTENDER_COUNT", defaultContenderCount);
var stock = ReadPositiveInt("MASKMAP_STOCK", defaultStock);
var requestsPerSecond = ReadPositiveInt(
    "MASKMAP_REQUESTS_PER_SECOND",
    defaultRequestsPerSecond);
var capacityStrategy = ReadCapacityStrategy();

if (stock > contenderCount)
{
    throw new InvalidOperationException("MASKMAP_STOCK cannot exceed MASKMAP_CONTENDER_COUNT.");
}

var repositoryRoot = FindRepositoryRoot();
var port = GetAvailableTcpPort();
var baseUrl = $"http://127.0.0.1:{port}";

Console.WriteLine("Starting SQL Server Testcontainer...");
await using var sqlServer = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest")
    .Build();
await sqlServer.StartAsync();

var connectionString = sqlServer.GetConnectionString() +
                       ";Initial Catalog=MaskMapLoadTests";

Console.WriteLine($"Starting MaskMap.Api at {baseUrl}...");
using var apiProcess = ManagedApiProcess.Start(
    repositoryRoot,
    baseUrl,
    connectionString,
    capacityStrategy);

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30)
};

await WaitForApiAsync(httpClient, apiProcess, TimeSpan.FromSeconds(30));

var prepareResponse = await httpClient.PostAsJsonAsync(
    "/api/test/last-inventory-competition/prepare",
    new CompetitionPreparation(contenderCount, stock));
await EnsureSuccessAsync(prepareResponse, "prepare competition data");

var issuedRequests = 0;
var created = 0;
var inventoryInsufficient = 0;
var unexpected = 0;

var scenario = Scenario.Create(
    $"last_inventory_competition_{capacityStrategy.ToLowerInvariant()}",
    async _ =>
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
    .WithTestName($"Last inventory competition - {capacityStrategy}")
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
    $"PASS ({capacityStrategy}): {contenderCount} contenders competed for {stock} items; " +
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

static string ReadCapacityStrategy()
{
    var strategy = Environment.GetEnvironmentVariable(
        "MASKMAP_RESERVATION_CAPACITY_STRATEGY") ?? "Cas";

    if (!strategy.Equals("Cas", StringComparison.OrdinalIgnoreCase) &&
        !strategy.Equals("Updlock", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "MASKMAP_RESERVATION_CAPACITY_STRATEGY must be 'Cas' or 'Updlock'.");
    }

    return strategy.Equals("Cas", StringComparison.OrdinalIgnoreCase)
        ? "Cas"
        : "Updlock";
}

static string FindRepositoryRoot()
{
    var candidates = new[]
    {
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    };

    foreach (var candidate in candidates)
    {
        var directory = new DirectoryInfo(candidate);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "MaskMap.Api",
                    "MaskMap.Api.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "Could not find the repository root containing MaskMap.Api/MaskMap.Api.csproj.");
}

static int GetAvailableTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task WaitForApiAsync(
    HttpClient client,
    ManagedApiProcess apiProcess,
    TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow.Add(timeout);

    while (DateTimeOffset.UtcNow < deadline)
    {
        if (apiProcess.HasExited)
        {
            throw new InvalidOperationException(
                "MaskMap.Api exited before becoming ready." +
                Environment.NewLine +
                apiProcess.GetRecentOutput());
        }

        try
        {
            using var response = await client.GetAsync("/swagger/index.html");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
            // Kestrel is still starting.
        }

        await Task.Delay(200);
    }

    throw new TimeoutException(
        "MaskMap.Api did not become ready within the configured timeout." +
        Environment.NewLine +
        apiProcess.GetRecentOutput());
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

internal sealed class ManagedApiProcess : IDisposable
{
    private const int MaximumCapturedLines = 100;
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();

    private ManagedApiProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += CaptureOutput;
        _process.ErrorDataReceived += CaptureOutput;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process.HasExited;

    public static ManagedApiProcess Start(
        string repositoryRoot,
        string baseUrl,
        string connectionString,
        string capacityStrategy)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var apiDirectory = Path.Combine(repositoryRoot, "MaskMap.Api");
        var apiAssembly = Path.Combine(
            apiDirectory,
            "bin",
            configuration,
            "net8.0",
            "MaskMap.Api.dll");

        if (!File.Exists(apiAssembly))
        {
            throw new FileNotFoundException(
                "MaskMap.Api was not built for the current configuration.",
                apiAssembly);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = apiDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(apiAssembly);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
        startInfo.Environment["ReservationCapacity__Strategy"] = capacityStrategy;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start MaskMap.Api.");
        return new ManagedApiProcess(process);
    }

    public string GetRecentOutput() => string.Join(Environment.NewLine, _output);

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(TimeSpan.FromSeconds(10));
        }

        _process.Dispose();
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is null)
        {
            return;
        }

        _output.Enqueue(eventArgs.Data);
        while (_output.Count > MaximumCapturedLines)
        {
            _output.TryDequeue(out _);
        }
    }
}
