import { useEffect, useState } from 'react';
import * as api from '../api';
import { useSettings } from '../settings/SettingsContext';
import type { PosSettingsDto } from '../types';
import { intValue, parseIntInput, type IntInput } from '../utils/numberInput';

export function SettingsPage() {
  const settings = useSettings();
  const [model, setModel] = useState<PosSettingsDto>({
    storeName: '',
    currencyLabel: 'EGP',
    receiptAddress: '',
    receiptLandline: '',
    receiptPhone: '',
    lowStockThreshold: 5,
    allowSellerDiscount: false,
  });
  const [backupWebhook, setBackupWebhook] = useState('');
  const [backupHours, setBackupHours] = useState(24);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [statusError, setStatusError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [backupBusy, setBackupBusy] = useState(false);
  const [restoreFile, setRestoreFile] = useState<File | null>(null);
  const [restoreBusy, setRestoreBusy] = useState(false);
  const [lowStockInput, setLowStockInput] = useState<IntInput>(5);

  useEffect(() => {
    void (async () => {
      await settings.load();
      setModel({
        storeName: settings.storeName,
        currencyLabel: settings.currencyLabel,
        receiptAddress: settings.receiptAddress,
        receiptLandline: settings.receiptLandline,
        receiptPhone: settings.receiptPhone,
        lowStockThreshold: settings.lowStockThreshold,
        allowSellerDiscount: settings.allowSellerDiscount,
      });
      setLowStockInput(settings.lowStockThreshold);
      try {
        const backup = await api.getBackupSettings();
        setBackupWebhook(backup.discordWebhookUrl);
        setBackupHours([12, 24, 48].includes(backup.backupIntervalHours) ? backup.backupIntervalHours : 24);
      } catch {
        /* defaults */
      }
    })();
  }, []);

  const saveStore = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.updateSettings({ ...model, lowStockThreshold: intValue(lowStockInput) });
      await settings.refresh();
      setStatus('Settings saved');
      setStatusError(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed');
    } finally {
      setBusy(false);
    }
  };

  const saveBackup = async () => {
    setBackupBusy(true);
    try {
      await api.updateBackupSettings({ discordWebhookUrl: backupWebhook, backupIntervalHours: backupHours });
      setStatus('Backup settings saved');
      setStatusError(false);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Save failed');
      setStatusError(true);
    } finally {
      setBackupBusy(false);
    }
  };

  const runBackup = async () => {
    setBackupBusy(true);
    try {
      const result = await api.runBackupNow();
      setStatus(result.message || (result.success ? 'Backup sent to Discord.' : 'Backup failed'));
      setStatusError(!result.success);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Backup failed');
      setStatusError(true);
    } finally {
      setBackupBusy(false);
    }
  };

  const downloadBackup = async () => {
    setBackupBusy(true);
    try {
      const { fileName, blob } = await api.downloadBackupArchive();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setStatus(`Downloaded ${fileName}`);
      setStatusError(false);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Download failed');
      setStatusError(true);
    } finally {
      setBackupBusy(false);
    }
  };

  const restoreBackup = async () => {
    if (!restoreFile) return;
    if (!window.confirm('Restore database from this backup? All current store data will be replaced. Continue?')) return;
    setRestoreBusy(true);
    try {
      const result = await api.restoreDatabaseBackup(restoreFile);
      setStatus(result.message || (result.success ? 'Database restored.' : 'Restore failed'));
      setStatusError(!result.success);
      if (result.success) {
        setRestoreFile(null);
        await settings.refresh();
      }
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Restore failed');
      setStatusError(true);
    } finally {
      setRestoreBusy(false);
    }
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">Settings</h1>
      {error && <p className="error">{error}</p>}
      {status && <div className={`inline-banner ${statusError ? 'inline-banner-err' : 'inline-banner-ok'}`}>{status}</div>}
      <div className="card">
        <div className="field">
          <label htmlFor="set-store">Store name</label>
          <input id="set-store" className="input touch-input" maxLength={256} value={model.storeName} onChange={(e) => setModel((m) => ({ ...m, storeName: e.target.value }))} />
        </div>
        <div className="field">
          <label htmlFor="set-curr">Currency</label>
          <input id="set-curr" className="input touch-input" maxLength={32} value={model.currencyLabel} onChange={(e) => setModel((m) => ({ ...m, currencyLabel: e.target.value }))} />
        </div>
        <div className="field">
          <label htmlFor="set-address">Receipt address</label>
          <textarea
            id="set-address"
            className="input touch-input"
            maxLength={512}
            rows={3}
            value={model.receiptAddress}
            onChange={(e) => setModel((m) => ({ ...m, receiptAddress: e.target.value }))}
            placeholder="Street, city — printed below store name"
          />
        </div>
        <div className="field">
          <label htmlFor="set-landline">Receipt landline</label>
          <input id="set-landline" className="input touch-input" maxLength={64} value={model.receiptLandline} onChange={(e) => setModel((m) => ({ ...m, receiptLandline: e.target.value }))} />
        </div>
        <div className="field">
          <label htmlFor="set-phone">Receipt phone</label>
          <input id="set-phone" className="input touch-input" maxLength={64} value={model.receiptPhone} onChange={(e) => setModel((m) => ({ ...m, receiptPhone: e.target.value }))} />
        </div>
        <div className="field">
          <label htmlFor="set-low">Low stock threshold</label>
          <input id="set-low" type="number" min={0} className="input touch-input" value={lowStockInput} onChange={(e) => setLowStockInput(parseIntInput(e.target.value))} />
        </div>
        <label className="check-row touch-target">
          <input type="checkbox" checked={model.allowSellerDiscount} onChange={(e) => setModel((m) => ({ ...m, allowSellerDiscount: e.target.checked }))} />
          Sellers can discount without manager PIN
        </label>
        <button type="button" className="btn primary touch-target" disabled={busy} onClick={() => void saveStore()}>
          {busy ? 'Saving…' : 'Save store settings'}
        </button>
      </div>
      <h2 className="section-title">Database backup</h2>
      <div className="card">
        <div className="field">
          <label>Discord webhook</label>
          <input type="password" className="input touch-input" value={backupWebhook} onChange={(e) => setBackupWebhook(e.target.value)} autoComplete="off" />
        </div>
        <div className="field">
          <label>Interval (hours)</label>
          <select className="input touch-input" value={backupHours} onChange={(e) => setBackupHours(parseInt(e.target.value, 10))}>
            <option value={12}>12</option>
            <option value={24}>24</option>
            <option value={48}>48</option>
          </select>
        </div>
        <div className="btn-row">
          <button type="button" className="btn secondary touch-target" disabled={backupBusy} onClick={() => void saveBackup()}>
            Save backup
          </button>
          <button type="button" className="btn primary touch-target" disabled={backupBusy} onClick={() => void runBackup()}>
            Run now
          </button>
          <button type="button" className="btn secondary touch-target" disabled={backupBusy} onClick={() => void downloadBackup()}>
            Download .zip
          </button>
        </div>
      </div>
      <h2 className="section-title">Restore from backup</h2>
      <p className="muted">
        Choose any backup file: a <code>store-backup-….zip</code>, a <code>.db</code> file, or a Discord download (macOS may save the zip as <code>.db</code> — that still works). A safety copy is saved in <code>backups/pre-restore-….zip</code> first.
      </p>
      <div className="card">
        <div className="field">
          <label htmlFor="restore-file">Backup file</label>
          <input
            id="restore-file"
            type="file"
            className="input touch-input"
            disabled={restoreBusy}
            onChange={(e) => setRestoreFile(e.target.files?.[0] ?? null)}
          />
          {restoreFile && <p className="muted small">Selected: {restoreFile.name}</p>}
        </div>
        <button
          type="button"
          className="btn secondary touch-target danger-btn"
          disabled={restoreBusy || !restoreFile}
          onClick={() => void restoreBackup()}
        >
          {restoreBusy ? 'Restoring…' : 'Restore database'}
        </button>
      </div>
    </div>
  );
}
