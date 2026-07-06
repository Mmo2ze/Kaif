namespace StoreShared.Sales;

/// <summary>Allocates sale-level discount across lines and computes partial refund amounts.</summary>
public static class RefundPricingCalculator
{
    public sealed record LinePricing(
        int SkuId,
        int Quantity,
        decimal GrossLineTotal,
        decimal NetLineTotal,
        decimal RefundUnitPrice);

    public static IReadOnlyList<LinePricing> BuildLinePricing(
        IReadOnlyList<(int SkuId, int Quantity, decimal UnitPrice)> items,
        decimal discountAmount,
        decimal saleTotalAmount)
    {
        if (items.Count == 0)
            return Array.Empty<LinePricing>();

        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);
        var remainingNetTotal = saleTotalAmount;
        var result = new List<LinePricing>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var grossLineTotal = item.UnitPrice * item.Quantity;
            var netLineTotal = grossLineTotal;

            if (discountAmount > 0 && subtotal > 0)
            {
                netLineTotal = index == items.Count - 1
                    ? remainingNetTotal
                    : Math.Round(
                        grossLineTotal - (discountAmount * (grossLineTotal / subtotal)),
                        2,
                        MidpointRounding.AwayFromZero);
            }

            if (netLineTotal < 0)
                netLineTotal = 0;

            remainingNetTotal -= netLineTotal;
            var refundUnitPrice = item.Quantity > 0
                ? Math.Round(netLineTotal / item.Quantity, 4, MidpointRounding.AwayFromZero)
                : 0m;

            result.Add(new LinePricing(item.SkuId, item.Quantity, grossLineTotal, netLineTotal, refundUnitPrice));
        }

        return result;
    }

    /// <summary>
    /// Refund amount for <paramref name="refundQuantity"/> units, respecting prior partial refunds on the same line.
    /// When refunding all remaining units, returns exactly what is left (fixes rounding drift).
    /// </summary>
    public static decimal ComputeRefundAmount(
        decimal netLineTotal,
        int originalQuantity,
        int refundQuantity,
        int alreadyRefundedQuantity,
        decimal alreadyRefundedAmount)
    {
        if (refundQuantity <= 0 || originalQuantity <= 0 || netLineTotal <= 0)
            return 0;

        var remainingQty = originalQuantity - alreadyRefundedQuantity;
        if (remainingQty <= 0)
            return 0;

        var qty = Math.Min(refundQuantity, remainingQty);
        if (qty >= remainingQty)
            return Math.Max(0, netLineTotal - alreadyRefundedAmount);

        return Math.Round(netLineTotal * qty / originalQuantity, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ResolveNetLineTotal(
        decimal netLineTotal,
        decimal refundUnitPrice,
        decimal refundLineTotalForAvailable,
        int originalQuantity,
        int quantityAvailable,
        int alreadyRefunded,
        decimal unitPrice)
    {
        if (netLineTotal > 0)
            return netLineTotal;

        if (quantityAvailable > 0 && refundLineTotalForAvailable > 0)
        {
            if (alreadyRefunded == 0 && quantityAvailable == originalQuantity)
                return refundLineTotalForAvailable;

            return originalQuantity > 0
                ? refundLineTotalForAvailable * originalQuantity / quantityAvailable
                : refundLineTotalForAvailable;
        }

        if (refundUnitPrice > 0 && originalQuantity > 0)
            return refundUnitPrice * originalQuantity;

        return unitPrice * originalQuantity;
    }

    public static decimal ComputeRefundAmount(LinePricing pricing, int refundQuantity, int alreadyRefundedQuantity, decimal alreadyRefundedAmount) =>
        ComputeRefundAmount(pricing.NetLineTotal, pricing.Quantity, refundQuantity, alreadyRefundedQuantity, alreadyRefundedAmount);
}
