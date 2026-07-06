/** Mirrors StoreShared.Sales.RefundPricingCalculator.ResolveNetLineTotal */
export function resolveNetLineTotal(
  netLineTotal: number,
  refundUnitPrice: number,
  refundLineTotalForAvailable: number,
  originalQuantity: number,
  quantityAvailable: number,
  alreadyRefunded: number,
  unitPrice: number,
): number {
  if (netLineTotal > 0) return netLineTotal;
  if (quantityAvailable > 0 && refundLineTotalForAvailable > 0) {
    if (alreadyRefunded === 0 && quantityAvailable === originalQuantity) return refundLineTotalForAvailable;
    return originalQuantity > 0
      ? (refundLineTotalForAvailable * originalQuantity) / quantityAvailable
      : refundLineTotalForAvailable;
  }
  if (refundUnitPrice > 0 && originalQuantity > 0) return refundUnitPrice * originalQuantity;
  return unitPrice * originalQuantity;
}

/** Mirrors StoreShared.Sales.RefundPricingCalculator (keep in sync). */
export function computeRefundAmount(
  netLineTotal: number,
  originalQuantity: number,
  refundQuantity: number,
  alreadyRefundedQuantity: number,
  alreadyRefundedAmount: number,
): number {
  if (refundQuantity <= 0 || originalQuantity <= 0 || netLineTotal <= 0) return 0;

  const remainingQty = originalQuantity - alreadyRefundedQuantity;
  if (remainingQty <= 0) return 0;

  const qty = Math.min(refundQuantity, remainingQty);
  if (qty >= remainingQty) return Math.max(0, netLineTotal - alreadyRefundedAmount);

  return Math.round((netLineTotal * qty) / originalQuantity * 100) / 100;
}
