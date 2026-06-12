/** Lets users clear a number field while typing (avoids forced 0). */
export type IntInput = number | '';

export function parseIntInput(raw: string): IntInput {
  if (raw === '') return '';
  const n = parseInt(raw, 10);
  return Number.isNaN(n) ? '' : n;
}

export function intValue(value: IntInput, fallback = 0): number {
  return value === '' ? fallback : value;
}
