// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
var ut = new UnitTests_v2();
ut.RegularCustomer_ShouldPay99PercentOfOriginalAmount();
ut.VipCustomer_ShouldPay90PercentOfOriginalAmount();

ut.BirthMonth_ShouldPay95PercentOfOriginalAmount();