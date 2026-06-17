# 以 AI 為師

此專案為和 GPT 5.5 討論軟體工程相關議題，衍伸下來的程式碼練習和概念澄清，AI 或許不會是最標準的答案，但仍有助於我從目前的階段更向上爬升。

## Issues

### AmountCalculatHandler

這題是我在跟 AI 討論Robert C. Martin的三大著作clean code, Agile Software Development: Principles, Patterns, and Practices, clean architecure，並請他出問題考我，他所提出的第一個問題，我把問題簡化如下：

某個電商系統原本只有一般會員與 VIP 會員，產品經理說，未來可能加入：

- 生日會員折扣
- 員工折扣
- 節慶活動折扣
- 不同國家的折扣政策
- 可同時套用多種折扣，但有些折扣不能併用

並探討這樣的概念，在程式中要如何設計。

### Clean Code 與函式拆分

```C#
public async Task PayAsync(Guid orderId, string cardNumber)
{
    var order = await _db.Orders.FindAsync(orderId);

    if (order == null)
        throw new Exception("Order not found");

    if (order.Status == OrderStatus.Paid)
        throw new Exception("Already paid");

    if (order.TotalAmount <= 0)
        throw new Exception("Invalid amount");

    var card = cardNumber.Replace(" ", "");

    if (card.Length != 16)
        throw new Exception("Invalid card");

    var result = await _paymentGateway.ChargeAsync(
        card,
        order.TotalAmount);

    if (!result.Success)
    {
        _logger.LogError(
            "Payment failed: {Message}",
            result.ErrorMessage);

        throw new Exception(result.ErrorMessage);
    }

    order.Status = OrderStatus.Paid;
    order.PaidAt = DateTime.UtcNow;
    order.TransactionId = result.TransactionId;

    await _db.SaveChangesAsync();

    await _emailService.SendPaymentSuccessAsync(
        order.CustomerEmail,
        order.Id,
        order.TotalAmount);
}
```

某位工程師依照《Clean Code》「函式應該短小、只做一件事」，重構成：

```C#
public async Task PayAsync(Guid orderId, string cardNumber)
{
    var order = await GetOrderAsync(orderId);
    ValidateOrder(order);
    var card = NormalizeAndValidateCard(cardNumber);
    var result = await ChargeAsync(card, order.TotalAmount);
    MarkAsPaid(order, result);
    await SaveAsync();
    await SendEmailAsync(order);
}
```

請回答：

1. 重構後是否一定比原本更 Clean？
2. `PayAsync` 現在是否真的只做「一件事」？
3. 「只做一件事」應該如何判斷？呼叫七個函式算不算做七件事？
4. `ValidateOrder` 將多個驗證放在一起，是否違反 SRP？
5. 付款成功、資料庫儲存成功，但寄信失敗時，整個 Use Case 應該算成功還是失敗？這是 Clean Code 問題，還是 Architecture 問題？

#### 啟發

##### `PayAsync` 是否只做一件事？

在適當的抽象層級上，`PayAsync` 可以被認為只做一件事：完成付款用例。

Robert C. Martin 對「一件事」有一種判斷方法：

> 如果方法中的步驟都位於方法名稱之下的一個抽象層級，那它就在做一件事。

### ISP 與 介面爆炸

>  ISP 的判斷是不應強迫 client 依賴它不需要的方法。
>
> 介面設計是否合理，要看這些契約是不是對 client 有自然、穩定且獨立的語意。

太小 -> 不穩定與獨立。太大 -> client 被迫依賴不需要的方法。

#### 合理的介面隔離

當介面：

- 對某一類 client 有明確用途；
- 方法通常一起被使用；
- 方法一起變動；
- 有清楚的語意名稱；
- 不迫使 client 依賴不需要的功能；
- 可以獨立實作或替換。

