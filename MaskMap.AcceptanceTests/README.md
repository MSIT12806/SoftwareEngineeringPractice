# MaskMap Acceptance Tests

這個專案放黑箱驗收測試，不實作 `MaskMap.Api` 的 production code。

測試會透過 HTTP 呼叫正在執行的 API，因此執行前需要先啟動 API：

```powershell
dotnet run --project .\MaskMap.Api\MaskMap.Api.csproj --urls http://localhost:5000
```

另一個 terminal 執行：

```powershell
dotnet test .\MaskMap.AcceptanceTests\MaskMap.AcceptanceTests.csproj --filter "TestCategory=InitialRequirements"
```

如果 API 跑在其他 URL，可以設定：

```powershell
$env:MASKMAP_API_BASE_URL = "http://localhost:5001"
dotnet test .\MaskMap.AcceptanceTests\MaskMap.AcceptanceTests.csproj --filter "TestCategory=InitialRequirements"
```

目前包含：

- `IR-001`：使用者可查詢附近藥局庫存。
- `IR-002`：使用者可成功建立預約，並占用庫存。
- `IR-003`：庫存不足時預約失敗，且不可占用庫存。
- `IR-004`：額度不足時預約失敗，且不可占用庫存。
- `IR-005`：同一預約操作重送不可重複預約。
- `IR-006`：同一操作識別不可被不同請求內容重用。
- `IR-007`：使用者只能查詢自己的預約紀錄。
- `IR-008`：藥局可回報進貨。
- `IR-009`：藥局可確認使用者領取。
- `IR-010`：藥局可調整庫存。
- `IR-011`：藥局只能查詢本藥局有效預約。
- `IR-012`：預約 24 小時後可由背景工作釋放庫存與額度。

這些測試預期會在 API 尚未實作前失敗。它們是驗收規格的可執行版本。

`IR-005` 和 `IR-006` 目前使用 `Idempotency-Key` 作為預設的重送防護策略。規格本身不強制一定使用 `Idempotency-Key`，也可以採用 `ClientOperationId`、reservation operation resource、自然唯一鍵或其他能證明同一操作重送不會重複生效的設計。若 production design 選擇不同策略，需同步調整這兩個測試的輸入與預期錯誤碼。

## Test-only API prerequisites

為了讓黑箱驗收測試可重複執行，測試環境需要提供以下 test-only endpoints。這些 endpoint 不應在正式環境啟用。

```http
POST /api/test/reset
```

重建測試資料，讓每個 test case 都從已知 seed data 開始。

```http
POST /api/test/clock
Content-Type: application/json
```

```json
{
  "utcNow": "2026-06-18T08:00:01Z"
}
```

設定測試用系統時間，讓過期預約測試不需要真的等待 24 小時。

```http
POST /api/test/jobs/release-expired-reservations
```

觸發過期預約回收背景工作。
