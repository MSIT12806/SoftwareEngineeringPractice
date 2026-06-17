# 初始需求自動化驗證規格

## 文件目的

本文件將 `README.md` 中「1. 初始需求」轉成可自動化驗證的測試規格。

目前目標不是直接實作完整系統，而是先定義：

1. API 格式
2. 測試資料
3. 自動化測試怎麼跑
4. 每個測試的預期結果
5. 哪些需求適合自動化測試，哪些只能透過壓力測試或人工設計審查驗證

此文件會作為後續建立 `MaskMap.Api.Tests` 的依據。

---

# 1. 驗證範圍

本階段只驗證 `README.md` 的「1. 初始需求」。

涵蓋範圍：

- 使用者登入後查詢附近藥局庫存
- 使用者建立預約
- 使用者查詢自己的預約紀錄
- 藥局回報進貨
- 藥局確認領取
- 藥局調整庫存
- 藥局查詢有效預約
- 每 7 天最多購買 3 片
- 預約 24 小時內有效
- 預約逾期自動取消
- 取消或逾期後釋放庫存與額度
- 不可超賣
- 不可重複使用額度
- 查詢庫存可容許短暫延遲
- 預約結果不可容許超賣或重複扣額度
- Client retry、timeout、背景工作重複執行時仍需可恢復
- 操作需可稽核

不在本階段驗證：

- 真實健保卡登入
- 真實地圖服務
- 真實簡訊、Email、Push 通知
- 真實藥局離線同步協定
- 微服務拆分、Outbox、Message Queue

---

# 2. API 草案

## 2.1 使用者身分

第一版測試不實作真實登入，使用測試 Header 模擬身分。

```http
X-Test-User-Id: user-001
```

藥局端操作使用：

```http
X-Test-Pharmacy-Id: pharmacy-001
```

若未來加入正式驗證，測試仍可保留同樣的業務驗證案例，只替換認證機制。

---

## 2.2 查詢附近藥局庫存

```http
GET /api/pharmacies?lat=25.0330&lng=121.5654&radiusKm=3
```

Response:

```json
{
  "items": [
    {
      "pharmacyId": "pharmacy-001",
      "name": "信義健康藥局",
      "address": "台北市信義區測試路 1 號",
      "distanceKm": 0.8,
      "inventories": [
        {
          "productId": "mask-adult",
          "productName": "成人口罩",
          "availableQuantity": 10,
          "lastUpdatedAt": "2026-06-17T08:00:00Z"
        }
      ]
    }
  ]
}
```

驗證重點：

- 查得到附近藥局。
- 回傳庫存數量。
- 庫存查詢允許短暫延遲，因此只驗證資料格式與合理性，不把它當作強一致性來源。

---

## 2.3 查詢指定藥局庫存

```http
GET /api/pharmacies/pharmacy-001/inventories
```

Response:

```json
{
  "pharmacyId": "pharmacy-001",
  "items": [
    {
      "productId": "mask-adult",
      "availableQuantity": 10,
      "reservedQuantity": 0,
      "physicalQuantity": 10,
      "lastUpdatedAt": "2026-06-17T08:00:00Z"
    }
  ]
}
```

驗證重點：

- 藥局庫存可查詢。
- 若使用持久化庫存模型，`physicalQuantity = availableQuantity + reservedQuantity + soldOrCollectedQuantity` 的稽核規則需要另外定義。

---

## 2.4 建立預約

初始需求真正要求的是：

```text
Client 因 timeout 或 retry 重送同一個預約操作時，不可造成重複預約、重複扣庫存或重複占用額度。
```

第一版 API 規格先保留 `Idempotency-Key` 作為建議做法，但它不是唯一可接受設計。

可接受的替代設計包含：

- Client 提供 `Idempotency-Key`。
- Client 提供 `ClientOperationId` 欄位。
- Server 先建立 reservation operation，再由 client 查詢 operation 狀態。
- 使用自然唯一鍵限制同一使用者同一期間只能有一筆有效預約。
- 其他能證明「同一操作重送不會重複生效」的設計。

若實作不使用 `Idempotency-Key` header，需在 API 文件中明確定義採用哪一種重送防護機制，並調整 IR-005 / IR-006 的測試輸入。

```http
POST /api/reservations
X-Test-User-Id: user-001
Idempotency-Key: 11111111-1111-1111-1111-111111111111
Content-Type: application/json
```

Request:

```json
{
  "pharmacyId": "pharmacy-001",
  "productId": "mask-adult",
  "quantity": 3
}
```

成功 Response:

```http
201 Created
```

```json
{
  "reservationId": "reservation-001",
  "status": "Reserved",
  "pharmacyId": "pharmacy-001",
  "productId": "mask-adult",
  "quantity": 3,
  "expiresAt": "2026-06-18T08:00:00Z"
}
```

庫存不足 Response:

```http
409 Conflict
```

```json
{
  "code": "InventoryInsufficient",
  "message": "Inventory is insufficient."
}
```

額度不足 Response:

```http
409 Conflict
```

```json
{
  "code": "QuotaExceeded",
  "message": "User quota is not enough."
}
```

重複 Idempotency Key 但 request body 不同：

```http
409 Conflict
```

```json
{
  "code": "IdempotencyKeyConflict",
  "message": "The same idempotency key was used with a different request."
}
```

驗證重點：

- 預約成功時，庫存與額度同步被占用。
- 預約失敗時，不可只扣庫存或只扣額度。
- 同一份庫存不可同時被多個人預約。
- 同一個人的額度不可重複使用。
- 同一 Idempotency Key 重送時不可重複建立預約。

---

## 2.5 查詢預約操作結果

此 API 是建議設計，不是初始需求的唯一解。

它用來處理：

```text
Server 已 commit，但 response 在網路途中遺失，Client 不知道預約是否成功。
```

如果實作採用其他方式讓 Client 查得出重送結果，例如直接查有效預約或查 `ClientOperationId`，也可以滿足需求。

```http
GET /api/reservation-operations/11111111-1111-1111-1111-111111111111
X-Test-User-Id: user-001
```

Response:

```json
{
  "idempotencyKey": "11111111-1111-1111-1111-111111111111",
  "status": "Succeeded",
  "reservationId": "reservation-001",
  "responseCode": 201
}
```

可能狀態：

- `Processing`
- `Succeeded`
- `Rejected`
- `Failed`

驗證重點：

- Client timeout 後能查詢操作結果。
- Server 已 commit 但 response 遺失時，Client 不需要重複建立預約。

---

## 2.6 查詢自己的預約紀錄

```http
GET /api/me/reservations
X-Test-User-Id: user-001
```

Response:

```json
{
  "items": [
    {
      "reservationId": "reservation-001",
      "status": "Reserved",
      "pharmacyId": "pharmacy-001",
      "productId": "mask-adult",
      "quantity": 3,
      "expiresAt": "2026-06-18T08:00:00Z"
    }
  ]
}
```

驗證重點：

- 使用者只能看到自己的預約。
- 預約狀態需正確反映 `Reserved`、`Collected`、`Cancelled`、`Expired`。

---

## 2.7 藥局回報進貨

```http
POST /api/pharmacy/inventory/receipts
X-Test-Pharmacy-Id: pharmacy-001
Content-Type: application/json
```

Request:

```json
{
  "productId": "mask-adult",
  "quantity": 100,
  "occurredAt": "2026-06-17T08:00:00Z"
}
```

Response:

```http
200 OK
```

```json
{
  "pharmacyId": "pharmacy-001",
  "productId": "mask-adult",
  "physicalQuantity": 100,
  "availableQuantity": 100,
  "reservedQuantity": 0
}
```

驗證重點：

- 進貨後庫存增加。
- 進貨操作需要留下稽核紀錄。

---

## 2.8 藥局確認領取

```http
POST /api/pharmacy/reservations/reservation-001/collect
X-Test-Pharmacy-Id: pharmacy-001
Content-Type: application/json
```

Request:

```json
{
  "userId": "user-001",
  "collectedAt": "2026-06-17T09:00:00Z"
}
```

Response:

```json
{
  "reservationId": "reservation-001",
  "status": "Collected",
  "collectedAt": "2026-06-17T09:00:00Z"
}
```

驗證重點：

- 只有有效的 `Reserved` 預約可以領取。
- 領取後不可再取消或過期釋放。
- 領取後使用者額度應從占用轉為已使用。
- 藥局只能確認自己藥局的預約。

---

## 2.9 藥局調整庫存

```http
POST /api/pharmacy/inventory/adjustments
X-Test-Pharmacy-Id: pharmacy-001
Content-Type: application/json
```

Request:

```json
{
  "productId": "mask-adult",
  "deltaQuantity": -2,
  "reason": "Damaged",
  "occurredAt": "2026-06-17T10:00:00Z"
}
```

Response:

```json
{
  "pharmacyId": "pharmacy-001",
  "productId": "mask-adult",
  "physicalQuantity": 98,
  "availableQuantity": 98,
  "reservedQuantity": 0
}
```

驗證重點：

- 調整不可導致可用庫存或實體庫存為負數。
- 調整操作需留下稽核紀錄。

---

## 2.10 藥局查詢有效預約

```http
GET /api/pharmacy/reservations?status=Reserved
X-Test-Pharmacy-Id: pharmacy-001
```

Response:

```json
{
  "items": [
    {
      "reservationId": "reservation-001",
      "userId": "user-001",
      "productId": "mask-adult",
      "quantity": 3,
      "status": "Reserved",
      "expiresAt": "2026-06-18T08:00:00Z"
    }
  ]
}
```

驗證重點：

- 藥局只能看到自己藥局的有效預約。
- 過期、取消、已領取的預約不應出現在有效預約清單中。

---

## 2.11 取消預約

```http
POST /api/reservations/reservation-001/cancel
X-Test-User-Id: user-001
```

Response:

```json
{
  "reservationId": "reservation-001",
  "status": "Cancelled"
}
```

驗證重點：

- 取消只允許從 `Reserved` 狀態轉移。
- 取消後釋放庫存與額度。
- 重複取消不可重複釋放庫存與額度。

---

## 2.12 觸發過期預約回收

第一版測試可使用測試專用 API 觸發背景工作，避免測試依賴真實排程時間。

```http
POST /api/test/jobs/release-expired-reservations
```

Response:

```json
{
  "scanned": 10,
  "released": 3,
  "skipped": 7
}
```

驗證重點：

- 逾期預約自動轉為 `Expired`。
- 逾期後釋放庫存與額度。
- 背景工作重複執行不可重複釋放。

正式環境不應暴露 `/api/test/*` API。

---

# 3. 測試資料

每次自動化測試應使用獨立資料庫或重建資料庫，避免測試互相影響。

## 3.1 使用者

```text
user-001：一般使用者，剩餘額度 3
user-002：一般使用者，剩餘額度 3
user-no-quota：額度已用完，剩餘額度 0
```

## 3.2 藥局

```text
pharmacy-001
- 名稱：信義健康藥局
- 地址：台北市信義區測試路 1 號
- 緯度：25.0330
- 經度：121.5654

pharmacy-002
- 名稱：大安平安藥局
- 地址：台北市大安區測試路 2 號
- 緯度：25.0260
- 經度：121.5430
```

`GET /api/pharmacies?lat=25.0330&lng=121.5654&radiusKm=3` 這類附近藥局查詢，會用藥局經緯度與查詢座標計算距離。

第一版測試只需要驗證：

- 在半徑內的藥局會被回傳。
- 半徑外的藥局不會被回傳。
- 回傳結果包含距離與庫存資訊。

## 3.3 商品

```text
mask-adult：成人口罩
```

## 3.4 初始庫存

```text
pharmacy-001 / mask-adult：10
pharmacy-002 / mask-adult：0
```

## 3.5 額度週期

額度週期是用來表達「每位使用者每 7 天最多購買 3 片口罩」的計算區間。

例如本測試先採用「固定 7 天週期」：

```text
period-2026w25
起始時間：2026-06-17T00:00:00Z
結束時間：2026-06-24T00:00:00Z
每人限制：3
```

意思是：

```text
同一位使用者在 2026-06-17T00:00:00Z 到 2026-06-24T00:00:00Z 之間，
最多只能成功占用或購買 3 片口罩。
```

測試中的 `UserQuota` 可以理解成：

```text
UserQuota
- UserId
- PeriodId
- LimitQuantity
- ReservedQuantity
- PurchasedQuantity
```

範例：

```text
user-001 / period-2026w25
- LimitQuantity = 3
- ReservedQuantity = 0
- PurchasedQuantity = 0
```

當 `user-001` 預約 2 片時：

```text
ReservedQuantity = 2
PurchasedQuantity = 0
剩餘可預約數 = 3 - 2 - 0 = 1
```

當他領取這 2 片後：

```text
ReservedQuantity = 0
PurchasedQuantity = 2
剩餘可預約數 = 3 - 0 - 2 = 1
```

第一版先使用固定週期，讓測試容易驗證。之後如果要改成「滾動 7 天」，同樣的需求仍成立，但資料模型與測試案例要再調整。

---

# 4. 自動化測試執行方式

## 4.1 建議測試專案

```text
MaskMap.Api.Tests
```

建議測試分類：

```text
InitialRequirements
- UserInventoryQueryTests
- ReservationCreationTests
- ReservationIdempotencyTests
- UserReservationQueryTests
- PharmacyInventoryTests
- PharmacyCollectionTests
- ReservationExpirationTests
- ReservationConsistencyTests
- AuditTrailTests
```

## 4.2 建議測試工具

第一版：

- xUnit
- ASP.NET Core `WebApplicationFactory`
- Testcontainers for SQL Server 或 SQLite

若要驗證交易、鎖、條件式更新與高併發，建議使用 SQL Server Testcontainer，而不是 EF Core InMemory。

## 4.3 執行指令

未來測試專案建立後，預計使用：

```powershell
dotnet test .\SoftwareEngineeringPractice.sln --filter "Category=InitialRequirements"
```

若要跑併發測試：

```powershell
dotnet test .\SoftwareEngineeringPractice.sln --filter "Category=Concurrency"
```

若要跑壓力測試，建議另外使用 NBomber 或 k6，不與一般 CI 測試混在一起。

---

# 5. 初始需求測試案例

## IR-001 使用者可查詢附近藥局庫存

需求來源：

- `1.2 使用者功能`：查詢附近藥局的口罩庫存
- `1.4 初始業務規則`：查詢庫存可以容許短暫延遲

步驟：

1. 建立 `pharmacy-001`，庫存 10。
2. 呼叫 `GET /api/pharmacies?lat=25.0330&lng=121.5654&radiusKm=3`。

預期結果：

- 回傳 `200 OK`。
- 回傳清單包含 `pharmacy-001`。
- `mask-adult.availableQuantity` 大於或等於 0。
- 不要求此數量一定等於最新交易後庫存，因為查詢可容許短暫延遲。

測試類型：

```text
Integration
```

---

## IR-002 使用者可成功建立預約

需求來源：

- `1.2 使用者功能`：線上預約指定藥局的口罩
- `1.4 初始業務規則`：每 7 天最多購買 3 片
- `1.5 一致性`

步驟：

1. `user-001` 剩餘額度為 3。
2. `pharmacy-001 / mask-adult` 可用庫存為 10。
3. 呼叫 `POST /api/reservations` 預約 3 片。

預期結果：

- 回傳 `201 Created`。
- 建立一筆 `Reservation`，狀態為 `Reserved`。
- `Reservation.ExpiresAt` 約等於建立時間加 24 小時。
- `Inventory.AvailableQuantity` 從 10 變成 7。
- `Inventory.ReservedQuantity` 從 0 變成 3。
- `UserQuota.ReservedQuantity` 從 0 變成 3。
- `UserQuota.PurchasedQuantity` 尚未增加。
- 寫入稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-003 庫存不足時預約失敗且不可扣額度

需求來源：

- `1.4 初始業務規則`：同一份庫存不可同時被多個人預約
- `1.5 一致性`：預約失敗時，不可只扣庫存或只扣額度

步驟：

1. `user-001` 剩餘額度為 3。
2. `pharmacy-002 / mask-adult` 可用庫存為 0。
3. 呼叫 `POST /api/reservations` 預約 1 片。

預期結果：

- 回傳 `409 Conflict`。
- 錯誤碼為 `InventoryInsufficient`。
- 不建立 `Reservation`。
- `Inventory` 數量不變。
- `UserQuota.ReservedQuantity` 不變。
- 寫入失敗操作稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-004 額度不足時預約失敗且不可扣庫存

需求來源：

- `1.4 初始業務規則`：同一個人的購買額度不可被重複使用
- `1.5 一致性`：預約失敗時，不可只扣庫存或只扣額度

步驟：

1. `user-no-quota` 剩餘額度為 0。
2. `pharmacy-001 / mask-adult` 可用庫存為 10。
3. 呼叫 `POST /api/reservations` 預約 1 片。

預期結果：

- 回傳 `409 Conflict`。
- 錯誤碼為 `QuotaExceeded`。
- 不建立 `Reservation`。
- `Inventory.AvailableQuantity` 不變。
- `UserQuota.ReservedQuantity` 不變。
- 寫入失敗操作稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-005 同一預約操作重送不可重複預約

需求來源：

- `1.5 可恢復性`：相同請求可能因重試而重複送達

驗證目的：

```text
不管實作是否使用 Idempotency Key，只要 Client 重送同一個預約操作，
系統就不可重複建立預約、重複扣庫存或重複占用額度。
```

預設測試版本使用 `Idempotency-Key` 表達「同一操作」。

若 production design 不採用 `Idempotency-Key`，此案例應改用該設計的操作識別方式，例如：

- `ClientOperationId`
- reservation operation resource
- 同一使用者同一期間有效預約唯一約束

步驟：

1. 使用 `Idempotency-Key: fixed-key-001` 建立預約 1 片。
2. 使用相同 UserId、相同 Idempotency Key、相同 Request Body 再送一次。

預期結果：

- 第一次回傳 `201 Created`。
- 第二次回傳前次結果，可為 `200 OK` 或 `201 Created`，但 response body 必須包含同一個 `reservationId`。
- 系統只建立一筆 `Reservation`。
- 庫存只扣 1 次。
- 額度只占用 1 次。
- 若實作有操作紀錄，該操作應只有一筆成功結果。

測試類型：

```text
Integration
```

---

## IR-006 同一操作識別不可被不同請求內容重用

需求來源：

- `1.5 可恢復性`
- `1.5 可稽核性`：同一個操作是否曾被重試

驗證目的：

```text
如果系統提供「操作識別」來支援重送防護，則同一操作識別不可被拿來代表兩個不同預約意圖。
```

此案例只適用於使用 `Idempotency-Key`、`ClientOperationId` 或 reservation operation resource 的設計。

如果實作完全不提供 client-visible operation id，而是只靠自然唯一鍵防止重複有效預約，則此案例可以改成：

```text
同一使用者在同一額度週期內已存在有效預約時，再預約另一間藥局應回傳明確衝突，
且不可額外扣庫存或額度。
```

步驟：

1. 使用 `Idempotency-Key: fixed-key-002` 預約 `pharmacy-001` 1 片。
2. 使用相同 Key 改成預約 `pharmacy-002` 1 片。

預期結果：

- 第二次回傳 `409 Conflict`。
- 錯誤碼為 `IdempotencyKeyConflict`。
- 不建立第二筆 `Reservation`。
- 不額外扣庫存或額度。
- 稽核紀錄可追查衝突。

測試類型：

```text
Integration
```

---

## IR-007 使用者可查詢自己的預約紀錄

需求來源：

- `1.2 使用者功能`：查詢自己的預約紀錄

步驟：

1. `user-001` 建立一筆預約。
2. `user-002` 建立一筆預約。
3. 使用 `user-001` 呼叫 `GET /api/me/reservations`。

預期結果：

- 回傳 `200 OK`。
- 清單包含 `user-001` 的預約。
- 清單不包含 `user-002` 的預約。

測試類型：

```text
Integration
```

---

## IR-008 藥局可回報進貨

需求來源：

- `1.3 藥局功能`：回報口罩進貨

步驟：

1. `pharmacy-001 / mask-adult` 初始庫存為 10。
2. 呼叫 `POST /api/pharmacy/inventory/receipts` 增加 100。

預期結果：

- 回傳 `200 OK`。
- `PhysicalQuantity` 增加 100。
- `AvailableQuantity` 增加 100。
- 寫入 Inventory Ledger 或 Audit Log。

測試類型：

```text
Integration
```

---

## IR-009 藥局可確認使用者領取

需求來源：

- `1.3 藥局功能`：確認使用者領取
- `1.2 使用者功能`：規定時間內前往藥局領取

步驟：

1. `user-001` 在 `pharmacy-001` 預約 3 片。
2. 藥局呼叫 `POST /api/pharmacy/reservations/{reservationId}/collect`。

預期結果：

- 回傳 `200 OK`。
- `Reservation.Status` 變成 `Collected`。
- `Inventory.ReservedQuantity` 減少 3。
- `Inventory.AvailableQuantity` 不增加，因為口罩已被領走。
- `UserQuota.ReservedQuantity` 減少 3。
- `UserQuota.PurchasedQuantity` 增加 3。
- 預約不可再取消或過期釋放。
- 寫入領取稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-010 藥局可調整庫存

需求來源：

- `1.3 藥局功能`：調整庫存
- `1.5 可稽核性`

步驟：

1. `pharmacy-001 / mask-adult` 初始庫存為 10。
2. 呼叫 `POST /api/pharmacy/inventory/adjustments` 調整 `-2`。

預期結果：

- 回傳 `200 OK`。
- `PhysicalQuantity` 減少 2。
- `AvailableQuantity` 減少 2。
- 不可出現負庫存。
- 寫入調整原因與稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-011 藥局可查詢本藥局有效預約

需求來源：

- `1.3 藥局功能`：查詢本藥局的有效預約

步驟：

1. `pharmacy-001` 有一筆 `Reserved` 預約。
2. `pharmacy-001` 有一筆 `Collected` 預約。
3. `pharmacy-002` 有一筆 `Reserved` 預約。
4. 使用 `X-Test-Pharmacy-Id: pharmacy-001` 查詢有效預約。

預期結果：

- 回傳 `200 OK`。
- 只包含 `pharmacy-001` 且狀態為 `Reserved` 的預約。
- 不包含 `Collected`、`Cancelled`、`Expired`。
- 不包含其他藥局的預約。

測試類型：

```text
Integration
```

---

## IR-012 預約 24 小時後自動過期並釋放資源

需求來源：

- `1.4 初始業務規則`：預約成功後，必須在 24 小時內領取
- `1.4 初始業務規則`：預約超過期限後，系統自動取消預約
- `1.5 一致性`

步驟：

1. `user-001` 預約 3 片。
2. 將測試時間推進到 `ExpiresAt` 之後。
3. 呼叫 `POST /api/test/jobs/release-expired-reservations`。

預期結果：

- `Reservation.Status` 變成 `Expired`。
- `Inventory.AvailableQuantity` 增加 3。
- `Inventory.ReservedQuantity` 減少 3。
- `UserQuota.ReservedQuantity` 減少 3。
- `UserQuota.PurchasedQuantity` 不增加。
- 寫入過期釋放稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-013 過期釋放重複執行不可重複加回庫存與額度

需求來源：

- `1.5 一致性`：預約取消或過期時，不可重複釋放庫存與額度
- `1.5 可恢復性`：排程或背景工作可能重複執行

步驟：

1. 建立一筆已過期但尚未釋放的預約。
2. 連續呼叫兩次 `POST /api/test/jobs/release-expired-reservations`。

預期結果：

- 第一次釋放成功。
- 第二次不再釋放同一筆預約。
- `Inventory.AvailableQuantity` 只增加一次。
- `UserQuota.ReservedQuantity` 只減少一次。
- 稽核紀錄可看出重複執行沒有造成重複釋放。

測試類型：

```text
Integration
```

---

## IR-014 最後一份庫存高併發預約不可超賣

需求來源：

- `1.4 初始業務規則`：同一份庫存不可同時被多個人預約
- `1.5 一致性`：不可超賣

步驟：

1. 設定 `pharmacy-001 / mask-adult` 可用庫存為 1。
2. 建立 100 個不同使用者，每人剩餘額度 3。
3. 同時送出 100 個 `POST /api/reservations`，每個請求預約 1 片。

預期結果：

- 只有 1 個請求成功。
- 其餘 99 個請求回傳 `409 Conflict`，錯誤碼為 `InventoryInsufficient`。
- 最終 `Inventory.AvailableQuantity = 0`。
- 最終 `Inventory.ReservedQuantity = 1`。
- 系統只建立 1 筆 `Reserved` 預約。
- 不可出現負庫存。

測試類型：

```text
Concurrency
```

---

## IR-015 同一使用者高併發預約不可超額使用額度

需求來源：

- `1.4 初始業務規則`：每位使用者每 7 天最多購買 3 片口罩
- `1.4 初始業務規則`：同一個人的購買額度不可被重複使用
- `1.5 一致性`：不可超額使用購買額度

步驟：

1. 設定 `user-001` 剩餘額度為 3。
2. 設定 `pharmacy-001 / mask-adult` 可用庫存為 100。
3. 同時送出 100 個 `POST /api/reservations`，每個請求預約 1 片。

預期結果：

- 最多 3 個請求成功。
- 其餘請求回傳 `409 Conflict`，錯誤碼為 `QuotaExceeded`。
- `UserQuota.ReservedQuantity` 最多為 3。
- `Inventory.ReservedQuantity` 最多增加 3。
- 不可建立超過 3 片總量的有效預約。

測試類型：

```text
Concurrency
```

---

## IR-016 領取與過期釋放不可同時成功

需求來源：

- `1.5 一致性`
- `1.5 可恢復性`

步驟：

1. 建立一筆接近到期的 `Reserved` 預約。
2. 同時觸發藥局領取與過期釋放。

預期結果：

- 最終狀態只能是 `Collected` 或 `Expired` 其中之一。
- 若為 `Collected`，不可釋放庫存回可用量。
- 若為 `Expired`，不可增加 `PurchasedQuantity`。
- 不可同時出現領取稽核與成功釋放稽核。

測試類型：

```text
Concurrency
```

---

## IR-017 額度檢查不可用時預約應 Fail Closed

需求來源：

- `1.5 可用性`：額度檢查功能可能暫時不可用
- `README.md 2.5`：Fail Closed

步驟：

1. 模擬額度檢查資料庫或服務暫時不可用。
2. 呼叫 `POST /api/reservations`。

預期結果：

- 回傳 `503 Service Unavailable` 或明確的 `QuotaUnavailable` 錯誤。
- 不建立預約。
- 不扣庫存。
- 不占用額度。
- 寫入失敗稽核紀錄。

測試類型：

```text
Integration
```

---

## IR-018 操作需可稽核

需求來源：

- `1.5 可稽核性`

步驟：

1. 建立預約。
2. 查詢該預約的稽核紀錄。

預期結果：

- 可追查預約建立時間。
- 可追查哪一筆操作占用庫存。
- 可追查哪一筆操作占用額度。
- 可追查 Idempotency Key。
- 若後續領取、取消或過期，也能追查對應操作。

測試類型：

```text
Integration
```

---

# 6. 壓力測試規格

壓力測試不應取代正確性測試。建議等 API 與資料庫交易完成後再建立。

## LT-001 庫存查詢壓力測試

目標：

- 驗證查詢庫存 API 在大量讀取下仍可接受。
- 因庫存查詢允許 30 秒延遲，可使用快取或讀模型。

預計工具：

```text
k6 或 NBomber
```

情境：

```text
1000 concurrent users
duration: 3 minutes
endpoint: GET /api/pharmacies?lat=25.0330&lng=121.5654&radiusKm=3
```

預期結果：

- error rate < 1%
- p95 latency < 500ms
- API 不回傳負庫存

---

## LT-002 熱門藥局預約壓力測試

目標：

- 驗證熱門庫存搶購時不超賣。
- 觀察交易鎖定造成的延遲。

情境：

```text
available quantity: 100
concurrent reservation requests: 5000
each request quantity: 1
```

預期結果：

- 成功預約數 = 100。
- 失敗預約數 = 4900。
- 不可出現負庫存。
- 不可出現超過 100 筆有效預約。
- p95 latency 與 DB lock wait 需記錄，但第一版不先設定硬性門檻。

---

## LT-003 過期回收批次壓力測試

目標：

- 驗證背景工作分批處理大量過期預約時，不造成長交易或阻塞正常預約。

情境：

```text
expired reservations: 50,000
batch size: 500
workers: 1, 2, 4
```

預期結果：

- 所有過期預約最終轉為 `Expired`。
- 庫存與額度各自只釋放一次。
- 正常預約 API 在回收期間仍可操作。
- 不可發生 deadlock storm。

---

# 7. 預期測試報告格式

每次自動化測試應輸出：

```text
Test Run
- Environment
- Database Provider
- Test Started At
- Test Finished At
- Passed
- Failed
- Skipped
```

每個案例至少包含：

```text
Requirement Id
Test Name
Test Type
Result
Key Assertions
Failure Reason
```

範例：

```text
IR-014
最後一份庫存高併發預約不可超賣
Type: Concurrency
Result: Passed
Assertions:
- success count = 1
- reserved reservations = 1
- available quantity = 0
- no negative inventory
```

---

# 8. 第一階段完成定義

第一階段不需要完成壓力測試工具，但需要完成所有正確性測試規格。

完成條件：

1. API contract 已定義。
2. 初始測試資料已定義。
3. `IR-001` 到 `IR-018` 皆有測試步驟與預期結果。
4. 明確標示哪些是 `Integration`，哪些是 `Concurrency`。
5. 後續可以依此建立 `MaskMap.Api.Tests`。
