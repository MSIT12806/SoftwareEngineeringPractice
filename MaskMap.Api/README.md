# 口罩實名制預約與庫存系統

## 文件目的

本文件整理目前針對「口罩實名制預約與庫存系統」的系統設計討論。

內容分成兩部分：

1. 初始需求
2. 以「我的設計」為前提所衍伸出的問題與設計議題

本文件不是最終答案，而是作為後續實作、驗證與重構的設計基準。

---

# 1. 初始需求

## 1.1 系統背景

政府希望建立一套口罩實名制系統，讓民眾可以查詢藥局庫存、線上預約，並在規定時間內前往指定藥局領取。

系統需要面對全台大量使用者，同時兼顧公平性、不可超賣、不可重複使用購買額度，以及藥局可能斷線等情境。

---

## 1.2 使用者功能

使用者可以：

1. 使用健保卡或其他身分驗證方式登入。
2. 查詢附近藥局的口罩庫存。
3. 線上預約指定藥局的口罩。
4. 查詢自己的預約紀錄。
5. 在預約成功後，於規定時間內前往藥局領取。

---

## 1.3 藥局功能

藥局可以：

1. 回報口罩進貨。
2. 確認使用者領取。
3. 調整庫存。
4. 查詢本藥局的有效預約。
5. 在網路暫時中斷後恢復操作。

---

## 1.4 初始業務規則

1. 每位使用者每 7 天最多購買 3 片口罩。
2. 預約成功後，必須在 24 小時內領取。
3. 預約超過期限後，系統自動取消預約。
4. 預約取消後，必須釋放：
   - 藥局庫存
   - 使用者已占用的購買額度
5. 同一份庫存不可同時被多個人預約。
6. 同一個人的購買額度不可被重複使用。
7. 查詢庫存可以容許短暫延遲，例如 30 秒。
8. 預約結果不能容許超賣或重複扣額度。

---

## 1.5 非功能需求

### 一致性

以下業務限制必須被保證：

- 不可超賣。
- 不可超額使用購買額度。
- 預約成功時，庫存與額度必須同步被占用。
- 預約失敗時，不可只扣庫存或只扣額度。
- 預約取消或過期時，不可重複釋放庫存與額度。

### 可用性

- 系統可能在開放預約後數分鐘內湧入大量流量。
- 身分驗證服務可能回應緩慢。
- 藥局端可能暫時斷線。
- 額度檢查功能可能暫時不可用。

### 可恢復性

- Client 可能因網路逾時，不知道預約到底是否成功。
- 相同請求可能因重試而重複送達。
- 排程或背景工作可能重複執行。
- 服務可能在處理到一半時中斷。

### 可稽核性

系統應能追查：

- 預約何時建立。
- 哪一筆操作占用了庫存。
- 哪一筆操作占用了額度。
- 預約何時被領取、取消或過期。
- 庫存與額度是否曾被補償或釋放。
- 同一個操作是否曾被重試。

---

# 2. 我的初始設計

## 2.1 強一致性交易邊界

我認為以下資料需要放在同一個強一致性交易中：

- 使用者購買額度
- 藥局實際庫存
- 預約紀錄
- 預約到期時間

原因是預約成功必須同時代表：

1. 使用者額度已被占用。
2. 藥局庫存已被占用。
3. 預約紀錄已建立。
4. 預約到期時間已確定。

如果其中任一項失敗，整筆預約都應該回滾。

概念上的交易如下：

```text
BEGIN TRANSACTION

確認使用者剩餘額度
占用使用者額度

確認藥局剩餘庫存
占用藥局庫存

建立預約紀錄
寫入預約到期時間

COMMIT
```

此設計優先保證一致性，並接受較高的耦合程度。

---

## 2.2 預約流程

### 防止超賣

建立預約時，在同一個交易內確認庫存並扣除庫存。

但不能只採用：

```text
SELECT 庫存
如果足夠
UPDATE 庫存
```

因為多個請求可能同時讀到相同庫存。

因此應採用條件式原子更新：

```sql
UPDATE Inventory
SET AvailableQuantity = AvailableQuantity - @Quantity
WHERE PharmacyId = @PharmacyId
  AND ProductId = @ProductId
  AND AvailableQuantity >= @Quantity;
```

再判斷：

```text
AffectedRows == 1
```

只有更新成功的請求可以繼續建立預約。

---

## 2.3 避免重複請求

Client 每次操作產生一個 GUID，作為 `Idempotency Key`。

後端保存該 Key，以辨識：

- 新操作
- 系統重試
- 重複送出的操作

概念資料如下：

```text
IdempotencyRecord
- UserId
- IdempotencyKey
- RequestHash
- Status
- ReservationId
- ResponsePayload
- CreatedAt
```

資料庫必須建立唯一約束：

```sql
UNIQUE(UserId, IdempotencyKey)
```

相同 Key 的處理規則：

```text
相同 Key + 相同 RequestHash
→ 回傳前次結果

相同 Key + 不同 RequestHash
→ 回傳衝突錯誤

相同 Key + 前次仍處理中
→ 回傳 Processing 或 202 Accepted
```

---

## 2.4 預約結果查詢

如果 Client 發送預約後逾時，不能直接把逾時當成失敗。

可能發生：

```text
Server 已 Commit
回應途中網路中斷
Client 收到 Timeout
```

因此系統提供操作結果查詢介面：

```http
GET /reservation-operations/{idempotencyKey}
```

可能回傳：

```text
Processing
Succeeded
Rejected
Failed
```

也可以提供使用者預約紀錄查詢，但使用 `Idempotency Key` 查詢會更精確。

---

## 2.5 額度功能不可用時的取捨

當系統無法確認使用者額度時，我選擇暫停預約。

理由：

- 無法確認資格時繼續接受預約，可能造成超額預約。
- 事後取消已成功的預約，會引發公平性與信任問題。
- 公共配給系統的一致性與可稽核性，比短暫可用性更重要。

此策略屬於：

```text
Fail Closed
```

也就是無法確認是否合法時，拒絕操作。

---

# 3. 以我的設計為前提衍伸的問題

## 3.1 交易邊界是否過大

把額度、庫存與預約放進同一個交易，可以簡化一致性問題，但會造成較高耦合。

額度通常依使用者分割：

```text
Quota(UserId, Period)
```

庫存通常依藥局與商品分割：

```text
Inventory(PharmacyId, ProductId)
```

兩者具有不同的資料擁有者與擴展方式。

因此需要思考：

1. 額度、預約、庫存是否應該位於同一個服務？
2. 是否應位於同一個資料庫？
3. 若拆成不同服務，是否接受最終一致性？
4. 是否需要分散式交易？
5. 是否值得為了服務自治增加 Outbox、Retry、補償與冪等處理？

目前的設計取向是：

> 優先採用單一服務、單一關聯式資料庫、單一交易，避免過早拆分微服務。

---

## 3.2 預約到期到底是儲存狀態，還是查詢時計算

我的第一個想法是：

> 不使用排程或訊息佇列去修改預約狀態，而是在查詢時根據 `ExpiresAt` 判斷預約是否仍有效。

例如可用庫存：

```text
可用庫存
= 實體庫存
- 尚未到期的有效預約數量
```

查詢概念：

```sql
SELECT
    i.PhysicalQuantity - COUNT(r.Id) AS AvailableQuantity
FROM Inventory i
LEFT JOIN Reservation r
    ON r.PharmacyId = i.PharmacyId
   AND r.ProductId = i.ProductId
   AND r.Status = 'Reserved'
   AND r.ExpiresAt > SYSUTCDATETIME()
WHERE i.PharmacyId = @PharmacyId
  AND i.ProductId = @ProductId
GROUP BY i.PhysicalQuantity;
```

### 衍生問題

#### 問題一：狀態語意分散

資料庫中可能仍然是：

```text
Status = Reserved
ExpiresAt < Now
```

但業務上已經是：

```text
Expired
```

所有查詢與流程都必須理解：

```text
Reserved 且 ExpiresAt <= Now
等同於 Expired
```

否則可能出現：

- 查詢顯示已過期
- 領取流程卻仍判斷為 Reserved
- 後台報表把它算成有效預約

#### 問題二：查詢成本

如果每次查詢可用庫存，都需要計算大量有效預約，熱門藥局可能產生較高負載。

可能需要索引：

```sql
CREATE INDEX IX_Reservation_Active
ON Reservation
(
    PharmacyId,
    ProductId,
    Status,
    ExpiresAt
);
```

#### 問題三：寫入仍需併發控制

即使可用庫存由查詢計算，建立新預約時仍需要防止多個請求同時建立。

因此仍需要：

- 鎖定庫存資源
- 條件式更新
- 或其他原子性的占用機制

不能只依賴查詢結果。

---

## 3.3 是否需要持久化可用庫存

可以考慮兩種模型。

### 模型 A：持久化可用庫存

```text
Inventory
- PhysicalQuantity
- AvailableQuantity
- ReservedQuantity
```

預約時：

```text
AvailableQuantity -= Quantity
ReservedQuantity += Quantity
```

取消或過期時：

```text
AvailableQuantity += Quantity
ReservedQuantity -= Quantity
```

優點：

- 查詢快速
- 條件式原子更新容易實作
- 適合熱門庫存搶購

缺點：

- 必須可靠處理釋放
- 排程漏執行可能造成庫存被永久占用
- 需要防止重複釋放

### 模型 B：可用庫存為衍生值

```text
AvailableQuantity
= PhysicalQuantity
- ActiveReservationQuantity
```

優點：

- 不需要靠排程修正可用庫存
- 到期狀態可根據時間自然失效

缺點：

- 查詢成本較高
- 寫入併發控制較複雜
- 所有流程都必須正確理解有效預約

目前尚未完全決定，但可考慮折衷：

> 持久化 `ReservedQuantity`，並在查詢時補償尚未被排程回收的過期預約。

---

## 3.4 預約釋放的冪等性

我提出建立 `ReleaseReservation` 資料表，記錄某筆預約是否已釋放。

資料表概念：

```sql
CREATE TABLE ReleaseReservation
(
    ReservationId UNIQUEIDENTIFIER NOT NULL,
    ReleasedAt DATETIME2 NOT NULL,
    Reason VARCHAR(30) NOT NULL,

    CONSTRAINT PK_ReleaseReservation
        PRIMARY KEY (ReservationId),

    CONSTRAINT FK_ReleaseReservation_Reservation
        FOREIGN KEY (ReservationId)
        REFERENCES Reservation(Id)
);
```

`ReservationId` 是 Primary Key，因此同一筆預約只能成功建立一筆釋放紀錄。

但如果預約、庫存、額度位於同一個資料庫，更簡單的方式是：

> 以 `Reservation.Status` 的條件式更新作為冪等閘門。

例如：

```sql
UPDATE Reservation
SET Status = 'Expired',
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @ReservationId
  AND Status = 'Reserved'
  AND ExpiresAt <= SYSUTCDATETIME();
```

只有第一次執行能將：

```text
Reserved → Expired
```

後續重複執行時，`AffectedRows = 0`，因此不可再次釋放庫存。

---

## 3.5 單一資料庫中的預約釋放交易

假設：

```text
Reservation
- Id
- UserId
- PharmacyId
- ProductId
- Quantity
- PeriodId
- Status
- ExpiresAt

Inventory
- PharmacyId
- ProductId
- AvailableQuantity
- ReservedQuantity

UserQuota
- UserId
- PeriodId
- ReservedQuantity
- PurchasedQuantity
```

釋放預約時應在同一個交易中完成：

1. 將預約從 `Reserved` 改成 `Expired`
2. 增加可用庫存
3. 減少庫存保留量
4. 釋放使用者額度
5. 寫入釋放紀錄

SQL Server 範例：

```sql
SET XACT_ABORT ON;

BEGIN TRANSACTION;

CREATE TABLE #ReleasedReservation
(
    UserId UNIQUEIDENTIFIER,
    PharmacyId UNIQUEIDENTIFIER,
    ProductId UNIQUEIDENTIFIER,
    Quantity INT,
    PeriodId INT
);

UPDATE Reservation
SET Status = 'Expired',
    UpdatedAt = SYSUTCDATETIME()
OUTPUT
    inserted.UserId,
    inserted.PharmacyId,
    inserted.ProductId,
    inserted.Quantity,
    inserted.PeriodId
INTO #ReleasedReservation
(
    UserId,
    PharmacyId,
    ProductId,
    Quantity,
    PeriodId
)
WHERE Id = @ReservationId
  AND Status = 'Reserved'
  AND ExpiresAt <= SYSUTCDATETIME();

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @UserId UNIQUEIDENTIFIER;
DECLARE @PharmacyId UNIQUEIDENTIFIER;
DECLARE @ProductId UNIQUEIDENTIFIER;
DECLARE @Quantity INT;
DECLARE @PeriodId INT;

SELECT
    @UserId = UserId,
    @PharmacyId = PharmacyId,
    @ProductId = ProductId,
    @Quantity = Quantity,
    @PeriodId = PeriodId
FROM #ReleasedReservation;

UPDATE Inventory
SET AvailableQuantity = AvailableQuantity + @Quantity,
    ReservedQuantity = ReservedQuantity - @Quantity
WHERE PharmacyId = @PharmacyId
  AND ProductId = @ProductId
  AND ReservedQuantity >= @Quantity;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 50001, 'Inventory reservation state is inconsistent.', 1;
END;

UPDATE UserQuota
SET ReservedQuantity = ReservedQuantity - @Quantity
WHERE UserId = @UserId
  AND PeriodId = @PeriodId
  AND ReservedQuantity >= @Quantity;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 50002, 'Quota reservation state is inconsistent.', 1;
END;

INSERT INTO ReleaseReservation
(
    ReservationId,
    ReleasedAt,
    Reason
)
VALUES
(
    @ReservationId,
    SYSUTCDATETIME(),
    'Expired'
);

COMMIT TRANSACTION;
```

此設計可保證：

- 只有第一個成功更新預約狀態的流程能釋放資源
- 庫存與額度不會只釋放一半
- 任一更新失敗時，整筆交易回滾

---

## 3.6 領取與到期同時發生

我認為業務上應優先保障已到藥局的使用者能領取。

但技術上必須先定義：

- 到期時間是嚴格截止時間
- 還是具有寬限期
- 判斷時間以 Client、藥局端或資料庫時間為準

### 嚴格依資料庫時間

領取：

```sql
UPDATE Reservation
SET Status = 'Collected',
    CollectedAt = SYSUTCDATETIME()
WHERE Id = @ReservationId
  AND Status = 'Reserved'
  AND ExpiresAt > SYSUTCDATETIME();
```

到期：

```sql
UPDATE Reservation
SET Status = 'Expired'
WHERE Id = @ReservationId
  AND Status = 'Reserved'
  AND ExpiresAt <= SYSUTCDATETIME();
```

兩者條件互斥：

```text
領取：ExpiresAt > Now
到期：ExpiresAt <= Now
```

兩個流程都只能從 `Reserved` 進行狀態轉移，因此最多只有一個成功。

此設計保證：

- 不會同時領取與釋放
- 到期前領取成功
- 到期後回收成功

但不保證「只要使用者站在櫃台就一定領取優先」。

### 寬限期

若業務希望保障櫃台操作延遲，可以設計：

```text
CustomerExpiresAt
ReleaseAfter
```

例如：

```text
CustomerExpiresAt = 21:00
ReleaseAfter = 21:15
```

使用者看到的期限是 21:00，但系統實際於 21:15 才釋放。

這比依賴排程剛好晚執行更清楚，因為寬限期本身是正式業務規則。

---

## 3.7 是否應修改原始業務規則

我認為可以透過調整規則降低系統複雜度。

原始規則：

```text
預約後 24 小時內領取
```

可以改成：

```text
預約僅限當日領取
藥局打烊後停止領取
凌晨統一釋放未領取預約
```

此修改可以降低：

- 大量不同到期時間的管理成本
- 延遲訊息數量
- 到期流程與領取流程的競態頻率
- 使用者對「24 小時」截止時間的理解成本

### 不建議每週釋放一次

如果未領取庫存一週後才釋放，會讓實際存在的口罩長時間無法再次預約。

較合理的方式是：

```text
每天打烊後或凌晨批次釋放
```

例如每批處理 500 筆：

```sql
SELECT TOP (500) Id
FROM Reservation
WHERE Status = 'Reserved'
  AND ExpiresAt <= SYSUTCDATETIME()
ORDER BY ExpiresAt;
```

避免單次處理大量資料造成：

- 長交易
- 大量鎖定
- Transaction Log 暴增
- 阻塞正常預約

---

# 4. Retry 設計

## 4.1 Retry 的目的

Retry 用來處理暫時性失敗，例如：

- 網路逾時
- HTTP 502、503、504
- 資料庫 Deadlock
- 暫時性連線中斷
- Rate Limit

Retry 不適合處理永久性錯誤，例如：

- 請求格式錯誤
- 預約不存在
- 權限不足
- 業務狀態不允許
- 資料已經不一致

永久性錯誤需要：

- 記錄錯誤
- 告警
- 人工介入
- 或補償流程

---

## 4.2 Retry 的前提：操作必須冪等

如果釋放庫存會因重試而執行多次，就可能重複增加庫存。

因此每次釋放操作都需要唯一的 `OperationId`：

```text
reservation-R001-expire
```

庫存服務保存：

```text
InventoryRelease
- OperationId
- ReservationId
- Quantity
- Status
- CreatedAt
```

並建立：

```sql
UNIQUE(OperationId)
```

同一個釋放操作無論送達多少次，都只能真正修改一次庫存。

---

## 4.3 指數退避與 Jitter

不應該持續立即重試，否則會壓垮故障中的服務。

可使用：

```text
第 1 次：1 秒後
第 2 次：2 秒後
第 3 次：4 秒後
第 4 次：8 秒後
第 5 次：16 秒後
```

並加入隨機延遲：

```text
8 秒 ± 2 秒
```

此策略稱為：

```text
Exponential Backoff + Jitter
```

Jitter 可以避免服務恢復時，所有失敗請求同時湧入。

---

## 4.4 持久化 Retry 工作

重試工作不能只存在記憶體中，否則服務重啟後會遺失。

可以建立：

```text
ReleaseJob
- Id
- ReservationId
- OperationId
- Status
- RetryCount
- NextRetryAt
- LastError
- CreatedAt
```

背景 Worker 查詢：

```sql
SELECT TOP (100) *
FROM ReleaseJob
WHERE Status IN ('Pending', 'Retrying')
  AND NextRetryAt <= SYSUTCDATETIME()
ORDER BY NextRetryAt;
```

失敗後更新：

```text
RetryCount += 1
NextRetryAt = Now + Backoff
Status = Retrying
LastError = 錯誤訊息
```

超過重試上限後：

```text
Status = DeadLetter
```

並發出告警。

---

# 5. 拆分服務後的問題

如果預約與庫存位於不同服務，無法使用同一個資料庫交易。

可能發生：

```text
1. Reservation 已改成 Expired
2. 準備呼叫 Inventory Service
3. Reservation Service 當機
4. 釋放庫存操作永久遺失
```

單純在記憶體中 Retry 無法解決此問題。

---

## 5.1 Transactional Outbox

應在修改預約狀態的同一個本地交易中，寫入待發送事件：

```sql
BEGIN TRANSACTION;

UPDATE Reservation
SET Status = 'Expired'
WHERE Id = @ReservationId
  AND Status = 'Reserved';

IF @@ROWCOUNT = 1
BEGIN
    INSERT INTO OutboxMessage
    (
        Id,
        MessageType,
        AggregateId,
        Payload,
        Status,
        CreatedAt
    )
    VALUES
    (
        NEWID(),
        'ReservationExpired',
        @ReservationId,
        @Payload,
        'Pending',
        SYSUTCDATETIME()
    );
END;

COMMIT TRANSACTION;
```

背景 Worker 再負責把 Outbox 訊息送給庫存服務。

---

## 5.2 三個機制的分工

```text
Outbox
→ 解決事件不能漏送

Retry
→ 解決暫時失敗後再次發送

Idempotent Consumer
→ 解決重複送達不能重複執行
```

三者不能互相取代。

---

# 6. 目前傾向的實作方向

第一版專案可採用較簡單且可驗證的方案：

1. 單一 ASP.NET Core 應用程式。
2. 單一關聯式資料庫。
3. 額度、庫存、預約放在同一個交易中。
4. 使用條件式原子 SQL 防止超賣。
5. 使用 `Idempotency Key` 防止重複預約。
6. 使用預約狀態轉移作為釋放操作的冪等閘門。
7. 使用背景排程分批掃描到期預約。
8. 領取與到期都只能從 `Reserved` 狀態轉移。
9. 以資料庫時間作為截止判斷依據。
10. 可考慮將規則調整為「當日領取，凌晨批次釋放」。
11. 第一版不拆微服務。
12. 後續再以 Outbox、Retry、冪等 Consumer 演進成分散式版本。

---

# 7. 待實作與驗證的問題

## 預約建立

- 如何設計資料表與唯一鍵？
- 如何保證額度與庫存同時被占用？
- 如何處理最後一份庫存的高併發搶購？
- SQL Transaction Isolation Level 應使用哪一種？
- 是否需要樂觀鎖版本欄位？

## 冪等性

- `Idempotency Key` 由 Client 或 Server 產生？
- Request Hash 如何計算？
- Processing 狀態卡住時如何恢復？
- Idempotency Record 保留多久？

## 到期釋放

- 排程執行頻率為何？
- 每批處理多少筆？
- 多個 Worker 是否能同時處理？
- 如何避免同一筆預約被兩個 Worker 選中？
- 失敗後如何重試？
- 超過重試上限如何告警？

## 領取流程

- 領取是否需要再次檢查使用者身分？
- 藥局斷線時能否離線領取？
- 到期前已開始操作，但到期後才送達 Server，如何判斷？
- 是否提供正式寬限期？

## 庫存模型

- 儲存 `AvailableQuantity` 還是動態計算？
- 進貨、人工調整、預約、領取應如何記錄？
- 是否需要 Inventory Transaction Ledger？
- 如何稽核庫存差異？

## 額度模型

- 7 天是滾動 7 天，還是固定週期？
- 預約占用額度，還是領取時才扣額度？
- 預約取消後是否立即釋放？
- 跨週期預約如何處理？

## 分散式演進

- 哪些條件出現後才值得拆微服務？
- 拆分後哪些一致性保證會改變？
- Outbox 由輪詢還是 CDC 發送？
- Consumer 如何保證冪等？
- 補償失敗如何進入 Dead Letter？

---

# 8. 核心設計原則

本系統目前採用以下原則：

1. 正確性優先於短暫可用性。
2. 優先以單一資料庫交易解決一致性問題。
3. 不以「排程剛好錯開」取代真正的競態控制。
4. 使用條件式狀態轉移保證原子性。
5. 所有可能重試的操作都必須具備冪等性。
6. 先檢討業務規則是否能簡化，再增加技術複雜度。
7. 第一版先完成可驗證的單體設計，再逐步演進為分散式架構。
