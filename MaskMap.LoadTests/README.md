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

Docker must be running. The load-test runner then performs the complete lifecycle:

1. starts an isolated SQL Server 2022 Testcontainer;
2. starts `MaskMap.Api` as a separate Kestrel process with the container connection string;
3. waits for the API to become ready;
4. prepares data, runs NBomber, and verifies the final database invariants;
5. stops the API and removes the container, including when the test fails.

Run the self-contained scenario with one command:

```powershell
dotnet run --project .\MaskMap.LoadTests\MaskMap.LoadTests.csproj -c Release
```

Optional environment variables:

```powershell
$env:MASKMAP_CONTENDER_COUNT = "10000"
$env:MASKMAP_STOCK = "100"
$env:MASKMAP_REQUESTS_PER_SECOND = "1000"
```

The preparation and verification endpoints are intentionally available only in the
Development environment. The API receives an isolated container database named
`MaskMapLoadTests`; no locally installed SQL Server or pre-created database is needed.

This setup is intended for concurrency correctness and performance regression testing.
Because NBomber, Kestrel, Docker, and SQL Server share the development machine, its RPS
is not a production-capacity measurement.
