# MaskMap load tests

## Last inventory competition

This NBomber scenario prepares 10,000 unique users with quota and makes them compete
for 100 units of the same pharmacy inventory. After the run it verifies all database
invariants, not only HTTP latency:

- exactly 100 reservations succeeded;
- exactly 9,900 requests were rejected with `InventoryInsufficient`;
- available inventory is zero and reserved inventory is 100;
- reservation count and occupied quota both equal 100;
- physical inventory remains unchanged and inventory is never oversold.

Start the API in the Development environment first:

```powershell
dotnet run --project .\MaskMap.Api\MaskMap.Api.csproj --urls http://localhost:5000
```

Run the scenario from another terminal:

```powershell
dotnet run --project .\MaskMap.LoadTests\MaskMap.LoadTests.csproj -c Release
```

Optional environment variables:

```powershell
$env:MASKMAP_API_BASE_URL = "http://localhost:5000"
$env:MASKMAP_CONTENDER_COUNT = "10000"
$env:MASKMAP_STOCK = "100"
$env:MASKMAP_REQUESTS_PER_SECOND = "1000"
```

The preparation and verification endpoints are intentionally available only in the
Development environment. The preparation step deletes and recreates the configured
database, so use a dedicated load-test database only.
