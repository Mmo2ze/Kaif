export type UserRole = 'admin' | 'seller';

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
}

export interface CurrentUserDto {
  id: number;
  username: string;
  role: UserRole;
}

export interface PosSettingsDto {
  storeName: string;
  currencyLabel: string;
  receiptAddress: string;
  receiptLandline: string;
  receiptPhone: string;
  lowStockThreshold: number;
  allowSellerDiscount: boolean;
}

export interface SkuDetailDto {
  id: number;
  barcode: string;
  productName: string;
  stock: number;
  unitPrice: number;
  salePrice: number | null;
  barcodePngBase64?: string;
}

export interface SaleLineRequest {
  skuId: number;
  quantity: number;
  unitPrice: number;
}

export interface CreateSaleRequest {
  items: SaleLineRequest[];
  discountAmount: number;
  discountAuthorizationToken?: string | null;
}

export interface SaleCreatedDto {
  saleId: number;
  totalAmount: number;
  receiptNumber: string;
}

export interface AuthorizeDiscountResponse {
  discountAuthorizationToken: string | null;
}

export interface SalesSummaryDto {
  todayRevenue: number;
  rangeTransactionCount: number;
  rangeAverageSale: number;
  topModelName: string | null;
  topModelQuantitySold: number;
  rangeTotalRevenue: number;
  rangeRefunded: number;
  rangeNetRevenue: number;
  todayRefunded: number;
  todayNetRevenue: number;
  rangeTotalCost: number;
  rangeNetProfit: number;
  todayTotalCost: number;
  todayNetProfit: number;
}

export interface SaleHistoryRowDto {
  id: number;
  receiptNumber: string;
  timestamp: string;
  cashierUsername: string;
  totalAmount: number;
  totalRefunded: number;
  isFullyRefunded: boolean;
}

export interface SaleLineDetailDto {
  productModelName: string;
  size: string;
  quantity: number;
  lineTotal: number;
}

export interface SaleHistoryDetailDto {
  lines: SaleLineDetailDto[];
}

export interface PagedSalesResult {
  items: SaleHistoryRowDto[];
  totalCount: number;
}

export interface StockRowDto {
  skuId: number;
  modelName: string;
  barcode: string;
  stock: number;
  barcodePngBase64?: string;
}

export interface SetStockRequest {
  quantity: number;
}

export interface AdjustStockRequest {
  quantity: number;
}

export interface StockAdjustmentDto {
  timestamp: string;
  modelName: string;
  quantityDelta: number;
  reason: string;
  performedBy: string;
}

export interface PagedStockAdjustmentsResult {
  items: StockAdjustmentDto[];
}

export interface ProductModelSummaryDto {
  id: number;
  name: string;
  description?: string | null;
  skuId: number;
  barcode: string;
  barcodePngBase64: string;
  stock: number;
  buyPrice: number;
  unitPrice: number;
  salePrice: number | null;
}

export interface CreateProductModelRequest {
  name: string;
  description?: string | null;
  buyPrice?: number;
  unitPrice?: number;
  salePrice?: number | null;
  initialStock?: number;
}

export interface UpdateProductModelRequest {
  name: string;
  description?: string | null;
}

export interface UpdateProductPriceRequest {
  buyPrice: number;
  unitPrice: number;
  salePrice: number | null;
}

export interface CatalogImportResultDto {
  success: boolean;
  message: string;
  productsCreated: number;
  productsUpdated: number;
  skusCreated: number;
  skusUpdated: number;
}

export interface UserAdminRowDto {
  id: number;
  username: string;
  role: UserRole;
  isActive: boolean;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  role: UserRole;
}

export interface LabelPrintQueueStatusDto {
  pendingCount: number;
}

export interface EnqueueLabelPrintRequest {
  barcode: string;
  count?: number;
}

export interface EnqueueLabelPrintResponse {
  jobId: string;
  message: string;
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface BackupSettingsAdminDto {
  discordWebhookUrl: string;
  backupIntervalHours: number;
}

export interface BackupSettingsUpdateDto {
  discordWebhookUrl: string;
  backupIntervalHours: number;
}

export interface BackupRunResponse {
  success: boolean;
  message: string;
}

export type RefundType = 'full' | 'partial';

export interface SaleByReceiptDto {
  receiptNumber: string;
  timestamp: string;
  cashierUsername: string;
  totalAmount: number;
  isFullyRefunded: boolean;
  lines: SaleLineRefundableDto[];
}

export interface SaleLineRefundableDto {
  skuId: number;
  productName: string;
  size: string;
  originalQuantity: number;
  quantityAvailable: number;
  alreadyRefunded: number;
  alreadyRefundedAmount: number;
  unitPrice: number;
  netLineTotal: number;
  refundUnitPrice: number;
  refundLineTotal: number;
}

export interface RefundLineDto {
  skuId: number;
  quantityToRefund: number;
}

export interface RefundRequestDto {
  receiptNumber: string;
  type: RefundType;
  lines?: RefundLineDto[] | null;
}

export interface RefundResultDto {
  success: boolean;
  amountRefunded: number;
  refundReceiptNumber: string;
  error?: string | null;
}
