const SALE_PREFIX = 'RCP-';
const REFUND_PREFIX = 'RFD-';

export function forSale(saleId: number): string {
  return `${SALE_PREFIX}${saleId.toString().padStart(5, '0')}`;
}

export function displayReceipt(row: { id: number; receiptNumber: string }): string {
  return row.receiptNumber?.trim() ? row.receiptNumber : forSale(row.id);
}

export function tryParseSaleId(input: string): number | null {
  const s = input.trim();
  let numeric: string | null = null;

  if (s.toLowerCase().startsWith(SALE_PREFIX.toLowerCase())) {
    numeric = s.slice(SALE_PREFIX.length);
  } else if (s.toLowerCase().startsWith(REFUND_PREFIX.toLowerCase())) {
    const rest = s.slice(REFUND_PREFIX.length);
    const dash = rest.indexOf('-');
    numeric = dash >= 0 ? rest.slice(0, dash) : rest;
  } else {
    return null;
  }

  const id = parseInt(numeric, 10);
  return id > 0 ? id : null;
}
