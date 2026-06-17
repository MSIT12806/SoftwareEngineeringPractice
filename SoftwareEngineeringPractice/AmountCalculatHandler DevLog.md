# AmountCalculatHandler 心路歷程

## V1

實作一般會員和VIP會員的折扣邏輯。

> 折扣計算沒有脫離業務語意的「最優通用解」，
> 只有在某一套明確的計價語言、計算階段與衝突規則下，比較合適的模型。

當我思考完 規格以後，我有個感想 就是即便是這種爛大街的折扣計算，似乎也沒有最優解，一切還是很受到整個公司如何定義這套計算流程的影響(也就是 domain modeling) 像這邊就有很多隱含的業務邏輯的訊息和語言，比方說，有加總前折扣和加總後折扣、以及你提到的訂單級折扣或訂單明細級折扣等等。

---

## V2

加入生日折扣的邏輯。

> TDD 能夠保護**內部實作變化**的重構，但如果改到的是**對外提供服務的方式**(contract)，就無法保護。

這時候可以先增加新的方法，但保留舊方法，用穩定的舊方法，去測試新方法，直到新方法也足夠穩定，再把舊方法改移除掉。

> 對 production code 重構的過程中，也會逐漸顯露出業務領域的語言和一些特徵。

我想分享一下，production code 從 V1 演變到 V2 的變化：

V1

```C#
    public decimal CalculateDiscount(
Customer customer,
decimal totalAmount)
    {
        if (customer.Type == CustomerType.Regular)
        {
            return totalAmount * 0.99m;
        }

        if (customer.Type == CustomerType.Vip)
        {
            return totalAmount * 0.90m;
        }

        return totalAmount;
    }
```

V2

```C#
    public decimal CalculateDiscount(
Customer customer,
decimal totalAmount)
    {
        decimal payableAmount = customer.Type switch
        {
            CustomerType.Regular => totalAmount * 0.99m,
            CustomerType.Vip => totalAmount * 0.90m,
            _ => totalAmount
        };

        BirthdayDiscount _birthdayDiscount = new BirthdayDiscount();
        payableAmount = _birthdayDiscount.Apply(
            customer,
            payableAmount);

        return payableAmount;
    }
```

可以看到幾個變化：

1. 原本業務邏輯比較簡單，因此採用`early return`方法來提高可讀性，但是這點明顯在第二版行不通。
2. 為了維持單元測試的原子性，我將生日折扣提升成一個類別，這樣`Apply`就可以單獨被測試案例所調用。
3. 因此慢慢產生屬於業務語言的某種聯繫：**會員折扣和生日折扣是不同的東西**。

## V3



