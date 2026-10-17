// The API's JSON shapes, copied field by field from the C# records (camelCase on the wire)
// and checked against the OpenAPI document. Keep them in sync when a DTO changes.

// C# enums are sent as their names (JsonStringEnumConverter), so they are string unions here.
export type PaymentType = "OneTime" | "Recurring";
export type TransactionStatus = "Pending" | "Succeeded" | "Failed";

// Checkout.Application.Dtos.CustomerDto
export interface CustomerDto {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
}

// Checkout.Application.Dtos.ProductDto
export interface ProductDto {
  id: number;
  name: string;
  description: string;
  price: number;
  currency: string;
  paymentType: PaymentType;
}

// Checkout.Application.Dtos.SavedCardDto (the customer token is never sent)
export interface SavedCardDto {
  id: number;
  maskedCardNo: string;
}

// Checkout.Api.Endpoints.CheckoutEndpoints.CheckoutRequest
export interface CheckoutRequest {
  customerId: number;
  productId: number;
  customerCardId: number | null;
}

// Checkout.Application.Checkout.CheckoutResult
export interface CheckoutResult {
  invoiceNo: string;
  status: TransactionStatus;
  redirectUrl: string | null;
}

// Checkout.Application.Dtos.TransactionStatusDto
export interface TransactionStatusDto {
  invoiceNo: string;
  status: TransactionStatus; // a C# string, but always one of the enum names
  customerId: number;
  productId: number;
  productName: string;
  amount: number;
  currencyCode: string;
  paymentChannel: string | null;
  maskedCardNo: string | null;
  respDesc: string | null;
  paidAt: string | null; // ISO 8601, UTC
  createdAt: string; // ISO 8601, UTC
}

// RFC 7807 problem details: the body of every error response from the API.
export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  traceId?: string;
  errors?: Record<string, string[]>;
}
