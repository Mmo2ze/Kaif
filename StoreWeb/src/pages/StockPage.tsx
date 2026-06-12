import { useEffect, useMemo, useState } from 'react';
import * as api from '../api';
import { useMobileLayout } from '../hooks/useMobileLayout';
import { useSettings } from '../settings/SettingsContext';
import type { StockAdjustmentDto, StockRowDto } from '../types';
import { intValue, parseIntInput, type IntInput } from '../utils/numberInput';

function daysAgoIso(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

export function StockPage() {
  const settings = useSettings();
  const isMobile = useMobileLayout();
  const [rows, setRows] = useState<StockRowDto[] | null>(null);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [editingSkuId, setEditingSkuId] = useState<number | null>(null);
  const [editQty, setEditQty] = useState<IntInput>(0);
  const [addQtyBySku, setAddQtyBySku] = useState<Record<number, number>>({});
  const [adjustments, setAdjustments] = useState<StockAdjustmentDto[] | null>(null);
  const [printRow, setPrintRow] = useState<StockRowDto | null>(null);
  const [printCount, setPrintCount] = useState(1);

  const filtered = useMemo(() => {
    if (!rows) return [];
    const q = filter.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter((r) => r.modelName.toLowerCase().includes(q));
  }, [rows, filter]);

  const rowClass = (row: StockRowDto) => {
    if (row.stock === 0) return 'stock-zero';
    if (settings.lowStockThreshold > 0 && row.stock < settings.lowStockThreshold) return 'stock-low';
    return '';
  };

  const reload = async () => {
    setError(null);
    setLoading(true);
    setEditingSkuId(null);
    try {
      await settings.load();
      setRows(await api.getStock());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Load failed');
    } finally {
      setLoading(false);
    }
  };

  const loadAdjustments = async () => {
    const from = daysAgoIso(30);
    const to = new Date().toISOString().slice(0, 10);
    const res = await api.getStockAdjustments(from, to, 1, 50);
    setAdjustments(res.items);
  };

  useEffect(() => {
    void reload();
    void loadAdjustments();
  }, []);

  const getAddQty = (skuId: number) => addQtyBySku[skuId] ?? 1;

  const saveEdit = async (skuId: number) => {
    await api.setStock(skuId, { quantity: Math.max(0, intValue(editQty)) });
    setEditingSkuId(null);
    await reload();
  };

  const addStock = async (row: StockRowDto) => {
    const qty = getAddQty(row.skuId);
    await api.addStock(row.skuId, { quantity: qty });
    await reload();
    await loadAdjustments();
  };

  const printLabels = () => {
    if (isMobile || !printRow?.barcodePngBase64) return;
    const ok = window.storePrintBarcode?.(`data:image/png;base64,${printRow.barcodePngBase64}`, printCount);
    if (!ok) setError('Could not open print window (check pop-up blocker).');
    setPrintRow(null);
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">Stock</h1>
      <p className="muted">Search by model name. Tap stock to edit. Amber: below threshold; red: zero.</p>
      <div className="stock-toolbar filter-bar">
        <div className="field" style={{ flex: 1, minWidth: '100%', margin: 0 }}>
          <label className="filter-label" htmlFor="filter">
            Search model
          </label>
          <input id="filter" className="input touch-input filter-input" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Model name…" />
        </div>
        <button type="button" className="btn primary touch-target" onClick={() => void reload()} disabled={loading}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {!rows ? (
        <p>Loading…</p>
      ) : filtered.length === 0 ? (
        <div className="empty-state">
          <p className="empty-state-title">No stock items</p>
        </div>
      ) : (
        <div className="stock-cards">
          {filtered.map((row) => (
            <div key={row.skuId} className={`stock-card ${rowClass(row)}`}>
              <div className="stock-card-head">
                <strong>{row.modelName}</strong>
                <span className="stock-card-size">{row.size}</span>
              </div>
              <div className="mono small">{row.barcode}</div>
              {row.barcodePngBase64 && (
                <div className="stock-card-barcode">
                  <img src={`data:image/png;base64,${row.barcodePngBase64}`} className="stock-barcode-thumb" alt="" />
                  {!isMobile && (
                    <button type="button" className="btn secondary touch-target" onClick={() => { setPrintRow(row); setPrintCount(1); }}>
                      Print label
                    </button>
                  )}
                </div>
              )}
              <div className="stock-card-row">
                <span className="muted">Stock</span>
                {editingSkuId === row.skuId ? (
                  <input type="number" className="input touch-input qty-stepper-input" min={0} value={editQty} onChange={(e) => setEditQty(parseIntInput(e.target.value))} onKeyDown={(e) => e.key === 'Enter' && void saveEdit(row.skuId)} />
                ) : (
                  <button type="button" className="stock-card-qty-btn" disabled={editingSkuId != null} onClick={() => { setEditingSkuId(row.skuId); setEditQty(row.stock); }}>
                    {row.stock}
                  </button>
                )}
              </div>
              <div className="btn-row">
                {editingSkuId === row.skuId ? (
                  <>
                    <button type="button" className="btn primary touch-target" onClick={() => void saveEdit(row.skuId)}>
                      Save
                    </button>
                    <button type="button" className="btn secondary touch-target" onClick={() => setEditingSkuId(null)}>
                      Cancel
                    </button>
                  </>
                ) : (
                  <>
                    <div className="qty-stepper" style={{ flex: 1 }}>
                      <button type="button" className="qty-stepper-btn" onClick={() => setAddQtyBySku((p) => ({ ...p, [row.skuId]: Math.max(1, getAddQty(row.skuId) - 1) }))}>
                        −
                      </button>
                      <span className="qty-stepper-value">{getAddQty(row.skuId)}</span>
                      <button type="button" className="qty-stepper-btn" onClick={() => setAddQtyBySku((p) => ({ ...p, [row.skuId]: getAddQty(row.skuId) + 1 }))}>
                        +
                      </button>
                    </div>
                    <button type="button" className="btn primary touch-target" onClick={() => void addStock(row)}>
                      Add
                    </button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
      <h2 className="section-title">Adjustment history</h2>
      {adjustments && adjustments.length > 0 ? (
        <div className="adjustment-cards">
          {adjustments.map((a, i) => (
            <div key={i} className="adjustment-card">
              <div className="adjustment-card-top">
                <strong>
                  {a.modelName} · {a.size}
                </strong>
                <span className={a.quantityDelta >= 0 ? 'badge' : 'badge badge-red'}>
                  {(a.quantityDelta > 0 ? '+' : '') + a.quantityDelta}
                </span>
              </div>
              <div className="muted small">
                {new Date(a.timestamp).toLocaleString()} · {a.performedBy}
              </div>
              <div className="mono small">{a.reason}</div>
            </div>
          ))}
        </div>
      ) : (
        <p className="muted">No adjustments in the last 30 days.</p>
      )}
      {!isMobile && printRow && (
        <>
          <div className="modal-backdrop" onClick={() => setPrintRow(null)} />
          <div className="modal-panel">
            <h2 className="modal-title">Print barcodes</h2>
            <p className="muted print-modal-summary">
              <strong>{printRow.modelName}</strong> · {printRow.size} · <span className="mono">{printRow.barcode}</span>
            </p>
            <div className="field">
              <label htmlFor="print-qty">How many labels?</label>
              <input id="print-qty" type="number" className="input" min={1} max={500} value={printCount} onChange={(e) => setPrintCount(parseInt(e.target.value, 10) || 1)} />
            </div>
            <div className="modal-actions">
              <button type="button" className="btn primary" onClick={printLabels}>
                Print
              </button>
              <button type="button" className="btn secondary" onClick={() => setPrintRow(null)}>
                Cancel
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
