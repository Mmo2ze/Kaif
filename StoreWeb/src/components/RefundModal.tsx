import { useEffect, useState } from 'react';
import * as api from '../api';
import { useSettings } from '../settings/SettingsContext';
import type { RefundType, SaleByReceiptDto } from '../types';
import { formatMoney } from '../utils/money';
import { intValue, parseIntInput, type IntInput } from '../utils/numberInput';
import { tryParseSaleId } from '../utils/receipt';
import { computeRefundAmount, resolveNetLineTotal } from '../utils/refundPricing';

interface PartialLine {
  skuId: number;
  productName: string;
  size: string;
  originalQuantity: number;
  available: number;
  alreadyRefunded: number;
  alreadyRefundedAmount: number;
  refundQty: IntInput;
  unitPrice: number;
  netLineTotal: number;
  maxForAvailable: number;
  refundUnitPrice: number;
  lineAmount: number;
  hasError: boolean;
}

interface Props {
  visible: boolean;
  initialReceipt?: string | null;
  onClose: () => void;
  onCompleted: () => void;
}

export function RefundModal({ visible, initialReceipt, onClose, onCompleted }: Props) {
  const settings = useSettings();
  const [step, setStep] = useState(1);
  const [receiptInput, setReceiptInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sale, setSale] = useState<SaleByReceiptDto | null>(null);
  const [refundType, setRefundType] = useState<RefundType>('full');
  const [partialLines, setPartialLines] = useState<PartialLine[]>([]);
  const [previewAmount, setPreviewAmount] = useState(0);
  const [lastResult, setLastResult] = useState<{ amountRefunded: number; refundReceiptNumber: string } | null>(null);

  const reset = () => {
    setStep(1);
    setReceiptInput('');
    setError(null);
    setSale(null);
    setPartialLines([]);
    setLastResult(null);
    setRefundType('full');
    setPreviewAmount(0);
  };

  useEffect(() => {
    if (!visible) return;
    reset();
    if (initialReceipt?.trim()) {
      setReceiptInput(initialReceipt.trim());
      void lookup(initialReceipt.trim());
    }
  }, [visible, initialReceipt]);

  const recalc = (s: SaleByReceiptDto | null, type: RefundType, lines: PartialLine[]) => {
    if (!s) return 0;
    if (type === 'full') {
      const available = s.lines.filter((l) => l.quantityAvailable > 0);
      const noPrior = s.lines.every((l) => l.alreadyRefunded === 0);
      return noPrior && available.length === s.lines.length
        ? s.totalAmount
        : available.reduce((sum, l) => sum + l.refundLineTotal, 0);
    }
    return lines.filter((l) => !l.hasError).reduce((sum, l) => sum + l.lineAmount, 0);
  };

  const lookup = async (input?: string) => {
    const value = (input ?? receiptInput).trim();
    setError(null);
    if (!value) {
      setError('Enter a Receipt ID.');
      return;
    }
    setBusy(true);
    try {
      const found = await api.getSaleByReceipt(value);
      if (found.isFullyRefunded) {
        setError('This receipt has already been fully refunded.');
        setSale(found);
        return;
      }
      const partial: PartialLine[] = found.lines.map((l) => ({
        skuId: l.skuId,
        productName: l.productName,
        size: l.size,
        originalQuantity: l.originalQuantity,
        available: l.quantityAvailable,
        alreadyRefunded: l.alreadyRefunded,
        alreadyRefundedAmount: l.alreadyRefundedAmount,
        refundQty: 0,
        unitPrice: l.unitPrice,
        netLineTotal: resolveNetLineTotal(
          l.netLineTotal,
          l.refundUnitPrice,
          l.refundLineTotal,
          l.originalQuantity,
          l.quantityAvailable,
          l.alreadyRefunded,
          l.unitPrice,
        ),
        maxForAvailable: l.refundLineTotal,
        refundUnitPrice: l.refundUnitPrice,
        lineAmount: 0,
        hasError: false,
      }));
      setSale(found);
      setPartialLines(partial);
      setRefundType('full');
      setPreviewAmount(recalc(found, 'full', partial));
      setStep(2);
    } catch {
      setError('Receipt ID not found — please check and try again.');
    } finally {
      setBusy(false);
    }
  };

  const onReceiptChange = (value: string) => {
    setReceiptInput(value);
    if (step === 1 && value.length >= 8 && tryParseSaleId(value)) {
      void lookup(value);
    }
  };

  const partialLineAmount = (line: PartialLine, refundQty: number) =>
    computeRefundAmount(
      line.netLineTotal,
      line.originalQuantity,
      refundQty,
      line.alreadyRefunded,
      line.alreadyRefundedAmount,
    );

  const updatePartialQty = (skuId: number, qty: IntInput) => {
    setPartialLines((prev) => {
      const n = intValue(qty);
      const next = prev.map((l) =>
        l.skuId === skuId
          ? {
              ...l,
              refundQty: qty,
              hasError: n > l.available,
              lineAmount: partialLineAmount(l, n),
            }
          : l,
      );
      setPreviewAmount(recalc(sale, refundType, next));
      return next;
    });
  };

  const selectRefundType = (type: RefundType) => {
    setRefundType(type);
    if (type !== 'partial') {
      setPreviewAmount(recalc(sale, type, partialLines));
      return;
    }

    const hasSelection = partialLines.some((l) => intValue(l.refundQty) > 0);
    const refundable = partialLines.filter((l) => l.available > 0);
    if (hasSelection || refundable.length !== 1 || refundable[0].available !== 1) {
      setPreviewAmount(recalc(sale, type, partialLines));
      return;
    }

    const next = partialLines.map((l) =>
      l.skuId === refundable[0].skuId
        ? {
            ...l,
            refundQty: 1,
            lineAmount: partialLineAmount(l, 1),
            hasError: false,
          }
        : l,
    );
    setPartialLines(next);
    setPreviewAmount(recalc(sale, type, next));
  };

  const confirm = async () => {
    if (!sale) return;
    setError(null);
    if (refundType === 'partial') {
      if (!partialLines.some((l) => intValue(l.refundQty) > 0 && !l.hasError)) {
        setError('Select at least one item to refund.');
        return;
      }
    }
    setBusy(true);
    try {
      const result = await api.processRefund({
        receiptNumber: sale.receiptNumber,
        type: refundType,
        lines:
          refundType === 'partial'
            ? partialLines.filter((l) => intValue(l.refundQty) > 0).map((l) => ({ skuId: l.skuId, quantityToRefund: intValue(l.refundQty) }))
            : null,
      });
      if (!result.success) {
        setError(result.error ?? 'Refund failed.');
        return;
      }
      setLastResult({ amountRefunded: result.amountRefunded, refundReceiptNumber: result.refundReceiptNumber });
      setStep(3);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Refund failed.');
    } finally {
      setBusy(false);
    }
  };

  if (!visible) return null;

  return (
    <>
      <div className="modal-backdrop" onClick={onClose} />
      <div className="modal-panel modal-panel-tall" onClick={(e) => e.stopPropagation()}>
        <h2>Process refund</h2>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        {step === 1 && (
          <>
            <p className="muted small">Scan the receipt barcode or type the Receipt ID (e.g. RCP-00123).</p>
            <div className="field">
              <label htmlFor="refund-receipt-id">Receipt ID</label>
              <input
                id="refund-receipt-id"
                className="input touch-input mono"
                value={receiptInput}
                onChange={(e) => onReceiptChange(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && void lookup()}
                autoComplete="off"
                placeholder="RCP-00001"
              />
            </div>
            <div className="btn-row">
              <button type="button" className="btn secondary touch-target" onClick={onClose}>
                Cancel
              </button>
              <button type="button" className="btn primary touch-target" disabled={busy} onClick={() => void lookup()}>
                {busy && <span className="spinner spinner-sm" aria-hidden="true" />}
                Next
              </button>
            </div>
          </>
        )}
        {step === 2 && sale && (
          <>
            <div className="refund-sale-summary">
              <p>
                <strong>{sale.receiptNumber}</strong> · {new Date(sale.timestamp).toLocaleString()} · {sale.cashierUsername}
              </p>
              <p className="muted small">Total paid: {formatMoney(sale.totalAmount, settings.currencyLabel)}</p>
            </div>
            <div className="refund-type-row">
              <label className="refund-type-option">
                <input type="radio" name="refund-type" checked={refundType === 'full'} onChange={() => selectRefundType('full')} />
                Full refund
              </label>
              <label className="refund-type-option">
                <input type="radio" name="refund-type" checked={refundType === 'partial'} onChange={() => selectRefundType('partial')} />
                Partial refund
              </label>
            </div>
            {refundType === 'partial' && (
              <>
                <p className="muted small">Enter refund qty per line. Amount includes sale discount.</p>
                <div className="refund-line-cards">
                {partialLines.map((line) => (
                  <div key={line.skuId} className={`refund-line-card ${line.hasError ? 'has-error' : ''}`}>
                    <div className="refund-line-card-head">
                      <strong>{line.productName}</strong>
                      <span className="muted">{line.size}</span>
                    </div>
                    <div className="muted small">{line.available} available</div>
                    <div className="refund-line-card-foot">
                      <div className="qty-stepper">
                        <button type="button" className="qty-stepper-btn" disabled={intValue(line.refundQty) <= 0} onClick={() => updatePartialQty(line.skuId, Math.max(0, intValue(line.refundQty) - 1))}>
                          −
                        </button>
                        <input
                          type="number"
                          className="input qty-stepper-input"
                          min={0}
                          max={line.available}
                          value={line.refundQty}
                          onChange={(e) => updatePartialQty(line.skuId, parseIntInput(e.target.value))}
                        />
                        <button type="button" className="qty-stepper-btn" disabled={intValue(line.refundQty) >= line.available} onClick={() => updatePartialQty(line.skuId, Math.min(line.available, intValue(line.refundQty) + 1))}>
                          +
                        </button>
                      </div>
                      <span>
                        {intValue(line.refundQty) > 0
                          ? formatMoney(line.lineAmount, settings.currencyLabel)
                          : line.available > 0
                            ? `1× ${formatMoney(
                                partialLineAmount(line, 1),
                                settings.currencyLabel,
                              )}`
                            : '—'}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
              </>
            )}
            <p className="refund-running-total">
              <strong>Refund amount:</strong> {formatMoney(previewAmount, settings.currencyLabel)}
            </p>
            <div className="btn-row">
              <button type="button" className="btn secondary touch-target" onClick={() => { setStep(1); setSale(null); }}>
                Back
              </button>
              <button type="button" className="btn primary touch-target" disabled={busy} onClick={() => void confirm()}>
                Refund {formatMoney(previewAmount, settings.currencyLabel)}
              </button>
            </div>
          </>
        )}
        {step === 3 && lastResult && (
          <>
            <p className="success-banner">Refund of {formatMoney(lastResult.amountRefunded, settings.currencyLabel)} processed.</p>
            <p className="mono muted small">{lastResult.refundReceiptNumber}</p>
            <div className="btn-row">
              <button
                type="button"
                className="btn primary touch-target"
                onClick={() => {
                  onCompleted();
                  onClose();
                }}
              >
                Done
              </button>
            </div>
          </>
        )}
      </div>
    </>
  );
}
