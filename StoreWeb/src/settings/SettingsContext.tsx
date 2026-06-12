import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import * as api from '../api';
import type { PosSettingsDto } from '../types';

const defaults: PosSettingsDto = {
  storeName: 'Kaif Store',
  currencyLabel: 'EGP',
  receiptLandline: '',
  receiptPhone: '',
  lowStockThreshold: 5,
  allowSellerDiscount: false,
};

interface SettingsContextValue extends PosSettingsDto {
  loaded: boolean;
  load: () => Promise<void>;
  refresh: () => Promise<void>;
}

const SettingsContext = createContext<SettingsContextValue | null>(null);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [settings, setSettings] = useState<PosSettingsDto>(defaults);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(async () => {
    try {
      const s = await api.getSettings();
      setSettings(s);
    } catch {
      setSettings(defaults);
    } finally {
      setLoaded(true);
    }
  }, []);

  const value = useMemo<SettingsContextValue>(
    () => ({
      ...settings,
      loaded,
      load,
      refresh: load,
    }),
    [settings, loaded, load],
  );

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}

export function useSettings(): SettingsContextValue {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used within SettingsProvider');
  return ctx;
}
