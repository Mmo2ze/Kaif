export function formatMoney(amount: number, currencyLabel: string): string {
  return `${amount.toFixed(2)} ${currencyLabel}`;
}
