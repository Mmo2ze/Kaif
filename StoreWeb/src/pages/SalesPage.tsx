import { useCallback, useEffect, useState } from 'react';
import * as api from '../api';
import { RefundModal } from '../components/RefundModal';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';
import type { PagedSalesResult, SaleHistoryDetailDto, SaleHistoryRowDto, SalesSummaryDto } from '../types';
import { downloadFile } from '../utils/download';
import { formatMoney } from '../utils/money';
import { displayReceipt } from '../utils/receipt';

const PAGE_SIZE = 15;

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

export function SalesPage() {
  const { isAdmin, user } = useAuth();
  const settings = useSettings();
  const [from, setFrom] = useState(todayIso());
  const [to, setTo] = useState(todayIso());
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [summary, setSummary] = useState<SalesSummaryDto | null>(null);
  const [pageResult, setPageResult] = useState<PagedSalesResult | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [details, setDetails] = useState<Record<number, SaleHistoryDetailDto>>({});
  const [refundOpen, setRefundOpen] = useState(false);
  const [refundPrefill, setRefundPrefill] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);

  const sellerId = isAdmin ? undefined : user?.id;
  const totalPages = pageResult && pageResult.totalCount > 0 ? Math.max(1, Math.ceil(pageResult.totalCount / PAGE_SIZE)) : 1;

  const load = useCallback(
    async (p = 1) => {
      setLoading(true);
      try {
        const [s, h] = await Promise.all([
          api.getSalesSummary(from, to, sellerId),
          api.getSalesHistory(from, to, sellerId, p, PAGE_SIZE),
        ]);
        setSummary(s);
        setPageResult(h);
        setPage(p);
        setExpanded(new Set());
        setDetails({});
      } finally {
        setLoading(false);
      }
    },
    [from, to, sellerId],
  );

  useEffect(() => {
    void load(1);
  }, [load]);

  const toggleDetail = async (id: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
    if (!details[id]) {
      const d = await api.getSaleDetail(id);
      setDetails((prev) => ({ ...prev, [id]: d }));
    }
  };

  const exportCsv = async () => {
    try {
      const blob = await api.exportSalesCsv(from, to);
      const buf = await blob.arrayBuffer();
      const b64 = btoa(String.fromCharCode(...new Uint8Array(buf)));
      downloadFile(`sales-${from.replace(/-/g, '')}.csv`, b64, 'text/csv');
      setStatus('Export downloaded');
    } catch {
      setStatus('Export failed');
    }
  };

  return (
    <div className="page-pad">
      <div className="page-head-row">
        <h1 className="page-title">{isAdmin ? 'Sales' : 'My sales'}</h1>
        {isAdmin && (
          <button type="button" className="btn secondary touch-target" onClick={() => { setRefundPrefill(null); setRefundOpen(true); }}>
            Refund
          </button>
        )}
      </div>
      {isAdmin && (
        <div className="filter-bar">
          <div className="field">
            <label>From</label>
            <input type="date" className="input touch-input" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="field">
            <label>To</label>
            <input type="date" className="input touch-input" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <button type="button" className="btn primary touch-target" disabled={loading} onClick={() => void load(1)}>
            Apply
          </button>
          <button type="button" className="btn secondary touch-target" disabled={loading} onClick={() => void exportCsv()}>
            Export
          </button>
        </div>
      )}
      {summary && (
        <section className="sales-money-panel">
          <div className="sales-money-hero">
            <span className="sales-money-hero-label">Net revenue (range)</span>
            <span className="sales-money-hero-value">{formatMoney(summary.rangeNetRevenue, settings.currencyLabel)}</span>
          </div>
          <div className="sales-money-breakdown">
            <div className="sales-money-row sales-money-row-gross">
              <span>Gross sales</span>
              <strong>{formatMoney(summary.rangeTotalRevenue, settings.currencyLabel)}</strong>
            </div>
            <div className="sales-money-row sales-money-row-refund">
              <span>Refunded</span>
              <strong>− {formatMoney(summary.rangeRefunded, settings.currencyLabel)}</strong>
            </div>
            <div className="sales-money-row">
              <span>Net profit</span>
              <strong>{formatMoney(summary.rangeNetProfit, settings.currencyLabel)}</strong>
            </div>
          </div>
          <div className="stat-row">
            <div className="stat-chip">
              <span className="muted">Transactions</span>
              <strong>{summary.rangeTransactionCount}</strong>
            </div>
            <div className="stat-chip">
              <span className="muted">Today net</span>
              <strong>{formatMoney(summary.todayNetRevenue, settings.currencyLabel)}</strong>
            </div>
            <div className="stat-chip">
              <span className="muted">Today profit</span>
              <strong>{formatMoney(summary.todayNetProfit, settings.currencyLabel)}</strong>
            </div>
          </div>
        </section>
      )}
      {status && <div className="inline-banner inline-banner-ok">{status}</div>}
      {loading && !pageResult ? (
        <p className="muted loading-with-spinner">
          <span className="spinner spinner-sm" aria-hidden="true" />
          Loading sales…
        </p>
      ) : pageResult && pageResult.items.length === 0 ? (
        <div className="empty-state">
          <span className="empty-state-icon" aria-hidden="true">
            ◎
          </span>
          <p className="empty-state-title">No sales in this range</p>
        </div>
      ) : (
        pageResult?.items.map((row: SaleHistoryRowDto) => (
          <div key={row.id}>
            <div className="card sale-card" onClick={() => void toggleDetail(row.id)}>
              <div className="sale-card-top">
                <span className="mono receipt-id">{displayReceipt(row)}</span>
                {row.isFullyRefunded && <span className="badge badge-red">Refunded</span>}
                {!row.isFullyRefunded && row.totalRefunded > 0 && <span className="badge badge-amber">Partial</span>}
                {isAdmin && !row.isFullyRefunded && (
                  <button
                    type="button"
                    className="btn secondary touch-target sale-refund-btn"
                    onClick={(e) => {
                      e.stopPropagation();
                      setRefundPrefill(displayReceipt(row));
                      setRefundOpen(true);
                    }}
                  >
                    Refund
                  </button>
                )}
              </div>
              <div className="muted small">
                {new Date(row.timestamp).toLocaleString()} · {row.cashierUsername}
              </div>
              <div className="sale-card-total">
                <strong>{formatMoney(row.totalAmount, settings.currencyLabel)}</strong>
              </div>
            </div>
            {expanded.has(row.id) && details[row.id] && (
              <div className="card card-inset">
                {details[row.id].lines.map((ln, i) => (
                  <div key={i} className="line-row">
                    <span>
                      {ln.productModelName} {ln.size} × {ln.quantity}
                    </span>
                    <span>{formatMoney(ln.lineTotal, settings.currencyLabel)}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        ))
      )}
      {pageResult && pageResult.items.length > 0 && (
        <div className="pager">
          <button type="button" className="btn secondary touch-target" disabled={page <= 1} onClick={() => void load(page - 1)}>
            Prev
          </button>
          <span className="muted">
            {page} / {totalPages}
          </span>
          <button type="button" className="btn secondary touch-target" disabled={page >= totalPages} onClick={() => void load(page + 1)}>
            Next
          </button>
        </div>
      )}
      <RefundModal
        visible={refundOpen}
        initialReceipt={refundPrefill}
        onClose={() => { setRefundOpen(false); setRefundPrefill(null); }}
        onCompleted={() => { setRefundOpen(false); setRefundPrefill(null); void load(1); }}
      />
    </div>
  );
}
