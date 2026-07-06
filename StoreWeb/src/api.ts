import type {
  AuthorizeDiscountResponse,
  BackupRunResponse,
  BackupSettingsAdminDto,
  BackupSettingsUpdateDto,
  CatalogImportResultDto,
  CreateProductModelRequest,
  UpdateProductModelRequest,
  CreateSaleRequest,
  CreateUserRequest,
  CurrentUserDto,
  LoginRequest,
  LoginResponse,
  PagedSalesResult,
  PagedStockAdjustmentsResult,
  PosSettingsDto,
  ProductModelSummaryDto,
  RefundRequestDto,
  RefundResultDto,
  EnqueueLabelPrintRequest,
  EnqueueLabelPrintResponse,
  LabelPrintQueueStatusDto,
  ResetPasswordRequest,
  SaleByReceiptDto,
  SaleCreatedDto,
  SaleHistoryDetailDto,
  SalesSummaryDto,
  SetStockRequest,
  AdjustStockRequest,
  SkuDetailDto,
  StockRowDto,
  UpdateProductPriceRequest,
  UserAdminRowDto,
} from './types';

const TOKEN_KEY = 'storeweb_token';
const USER_KEY = 'storeweb_user';

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getStoredUser(): CurrentUserDto | null {
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as CurrentUserDto;
  } catch {
    return null;
  }
}

export function saveSession(token: string, user: CurrentUserDto): void {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getStoredToken();
  const headers = new Headers(init?.headers);
  if (!headers.has('Content-Type') && init?.body) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const res = await fetch(path, { ...init, headers });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed (${res.status})`);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export async function login(body: LoginRequest): Promise<LoginResponse> {
  return apiFetch<LoginResponse>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function getMe(): Promise<CurrentUserDto> {
  return apiFetch<CurrentUserDto>('/api/auth/me');
}

export async function getHealth(): Promise<{ status: string }> {
  return apiFetch('/api/health');
}

export async function getSettings(): Promise<PosSettingsDto> {
  return apiFetch<PosSettingsDto>('/api/settings');
}

export async function updateSettings(body: PosSettingsDto): Promise<void> {
  await apiFetch('/api/settings', { method: 'PUT', body: JSON.stringify(body) });
}

export async function getBackupSettings(): Promise<BackupSettingsAdminDto> {
  return apiFetch<BackupSettingsAdminDto>('/api/settings/backup');
}

export async function updateBackupSettings(body: BackupSettingsUpdateDto): Promise<void> {
  await apiFetch('/api/settings/backup', { method: 'PUT', body: JSON.stringify(body) });
}

export async function runBackupNow(): Promise<BackupRunResponse> {
  return apiFetch<BackupRunResponse>('/api/backup/run-now', { method: 'POST' });
}

export async function downloadBackupArchive(): Promise<{ fileName: string; blob: Blob }> {
  const token = getStoredToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const res = await fetch('/api/backup/download', { headers });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Download failed (${res.status})`);
  }
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') ?? '';
  const match = disposition.match(/filename=\"?([^\";]+)\"?/i);
  const fileName = match?.[1]?.trim() || 'store-backup.zip';
  return { fileName, blob };
}

export async function restoreDatabaseBackup(file: File): Promise<BackupRunResponse> {
  const token = getStoredToken();
  const form = new FormData();
  form.append('file', file);
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const res = await fetch('/api/backup/restore', { method: 'POST', headers, body: form });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Restore failed (${res.status})`);
  }
  return (await res.json()) as BackupRunResponse;
}

export async function getSkuByBarcode(barcode: string, forLabelPrint = false): Promise<SkuDetailDto> {
  const suffix = forLabelPrint ? '?forLabelPrint=true' : '';
  return apiFetch<SkuDetailDto>(`/api/skus/${encodeURIComponent(barcode)}${suffix}`);
}

export async function authorizeDiscount(pin: string): Promise<AuthorizeDiscountResponse> {
  return apiFetch<AuthorizeDiscountResponse>('/api/pos/authorize-discount', {
    method: 'POST',
    body: JSON.stringify({ pin }),
  });
}

export async function createSale(body: CreateSaleRequest): Promise<SaleCreatedDto> {
  return apiFetch<SaleCreatedDto>('/api/sales', { method: 'POST', body: JSON.stringify(body) });
}

export async function getSalesSummary(from: string, to: string, sellerUserId?: number): Promise<SalesSummaryDto> {
  const q = new URLSearchParams({ from, to });
  if (sellerUserId != null) q.set('sellerUserId', String(sellerUserId));
  return apiFetch<SalesSummaryDto>(`/api/sales/summary?${q}`);
}

export async function getSalesHistory(
  from: string,
  to: string,
  sellerUserId: number | undefined,
  page: number,
  pageSize: number,
): Promise<PagedSalesResult> {
  const q = new URLSearchParams({ from, to, page: String(page), pageSize: String(pageSize) });
  if (sellerUserId != null) q.set('sellerUserId', String(sellerUserId));
  return apiFetch<PagedSalesResult>(`/api/sales?${q}`);
}

export async function getSaleDetail(id: number): Promise<SaleHistoryDetailDto> {
  return apiFetch<SaleHistoryDetailDto>(`/api/sales/${id}`);
}

export async function exportSalesCsv(from: string, to: string): Promise<Blob> {
  const q = new URLSearchParams({ from, to });
  const token = getStoredToken();
  const res = await fetch(`/api/sales/export?${q}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!res.ok) throw new Error('Export failed');
  return res.blob();
}

export async function getSaleByReceipt(receipt: string): Promise<SaleByReceiptDto> {
  return apiFetch<SaleByReceiptDto>(`/api/sales/receipt/${encodeURIComponent(receipt)}`);
}

export async function processRefund(body: RefundRequestDto): Promise<RefundResultDto> {
  return apiFetch<RefundResultDto>('/api/refunds', { method: 'POST', body: JSON.stringify(body) });
}

export async function getStock(): Promise<StockRowDto[]> {
  return apiFetch<StockRowDto[]>('/api/stock');
}

export async function setStock(skuId: number, body: SetStockRequest): Promise<void> {
  await apiFetch(`/api/stock/${skuId}`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function addStock(skuId: number, body: AdjustStockRequest): Promise<void> {
  await apiFetch(`/api/stock/${skuId}/add`, { method: 'POST', body: JSON.stringify(body) });
}

export async function getStockAdjustments(from: string, to: string, page: number, pageSize: number): Promise<PagedStockAdjustmentsResult> {
  const q = new URLSearchParams({ from, to, page: String(page), pageSize: String(pageSize) });
  return apiFetch<PagedStockAdjustmentsResult>(`/api/stock/adjustments?${q}`);
}

export async function getProducts(): Promise<ProductModelSummaryDto[]> {
  return apiFetch<ProductModelSummaryDto[]>('/api/products');
}

export async function exportProductsCatalog(): Promise<Blob> {
  const token = getStoredToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const res = await fetch('/api/products/export', { headers });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Export failed (${res.status})`);
  }
  return res.blob();
}

export async function importProductsCatalog(file: File): Promise<CatalogImportResultDto> {
  const token = getStoredToken();
  const form = new FormData();
  form.append('file', file);
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const res = await fetch('/api/products/import', { method: 'POST', headers, body: form });
  const body = (await res.json()) as CatalogImportResultDto;
  if (!res.ok) throw new Error(body.message || `Import failed (${res.status})`);
  return body;
}

export async function setProductStock(productId: number, body: SetStockRequest): Promise<void> {
  await apiFetch(`/api/products/${productId}/stock`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function addProductStock(productId: number, body: AdjustStockRequest): Promise<void> {
  await apiFetch(`/api/products/${productId}/stock/add`, { method: 'POST', body: JSON.stringify(body) });
}

export async function createProduct(body: CreateProductModelRequest): Promise<void> {
  await apiFetch('/api/products', { method: 'POST', body: JSON.stringify(body) });
}

export async function updateProduct(id: number, body: UpdateProductModelRequest): Promise<void> {
  await apiFetch(`/api/products/${id}`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function deleteProduct(id: number): Promise<void> {
  await apiFetch(`/api/products/${id}`, { method: 'DELETE' });
}

export async function updateProductPrice(modelId: number, body: UpdateProductPriceRequest): Promise<void> {
  await apiFetch(`/api/products/${modelId}/price`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function getUsers(): Promise<UserAdminRowDto[]> {
  return apiFetch<UserAdminRowDto[]>('/api/users');
}

export async function createUser(body: CreateUserRequest): Promise<void> {
  await apiFetch('/api/users', { method: 'POST', body: JSON.stringify(body) });
}

export async function resetPassword(userId: number, body: ResetPasswordRequest): Promise<void> {
  await apiFetch(`/api/users/${userId}/password`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function enqueueLabelPrint(body: EnqueueLabelPrintRequest): Promise<EnqueueLabelPrintResponse> {
  return apiFetch<EnqueueLabelPrintResponse>('/api/print/labels', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function getLabelPrintQueueStatus(): Promise<LabelPrintQueueStatusDto> {
  return apiFetch<LabelPrintQueueStatusDto>('/api/print/labels/status');
}

export async function isLabelPrintJobPending(jobId: string): Promise<boolean> {
  const res = await fetch(`/api/print/labels/${encodeURIComponent(jobId)}/pending`, {
    headers: getStoredToken() ? { Authorization: `Bearer ${getStoredToken()}` } : {},
  });
  if (res.status === 204) return false;
  if (res.ok) return true;
  throw new Error(await res.text());
}

export async function deactivateUser(userId: number): Promise<void> {
  await apiFetch(`/api/users/${userId}/deactivate`, { method: 'PUT' });
}
