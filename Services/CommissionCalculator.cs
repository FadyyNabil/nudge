namespace Marketplace.Services;

/// <summary>
/// Owner commission = price x percent / 100, rounded to 2 decimals.
/// This is only bookkeeping: the app does not move or charge real money.
/// </summary>
public static class CommissionCalculator
{
    public static decimal Calculate(decimal price, decimal percent) =>
        Math.Round(price * percent / 100m, 2, MidpointRounding.AwayFromZero);
}
