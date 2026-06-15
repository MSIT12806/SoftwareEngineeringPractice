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