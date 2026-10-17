import type {
  CheckoutRequest,
  CheckoutResult,
  CustomerDto,
  ProblemDetails,
  ProductDto,
  SavedCardDto,
  TransactionStatusDto,
} from "./types";

// NEXT_PUBLIC_ variables are copied into the browser bundle when the app is built, so this
// must never hold a secret. It's just where the API lives.
const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;

/** A non-2xx response from the API, with the message from its problem details. */
export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

// Every call goes through here: one place for the base URL, no caching, and error parsing.
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  if (!baseUrl) {
    throw new Error("NEXT_PUBLIC_API_BASE_URL is not set. Copy frontend/.env.example to frontend/.env.local.");
  }

  // "no-store": prices, cards and payment status must always be fresh, never a cached copy.
  const response = await fetch(`${baseUrl}${path}`, { ...init, cache: "no-store" });

  if (!response.ok) {
    throw await toApiError(response);
  }

  const data: unknown = await response.json();
  return data as T;
}

// The API answers every error with problem details; show its detail (or title), never raw JSON.
async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as ProblemDetails;
    const message = problem.detail ?? problem.title ?? `Request failed (HTTP ${response.status}).`;
    return new ApiError(message, response.status);
  } catch {
    // Not JSON (e.g. a proxy error page): fall back to a generic message.
    return new ApiError(`Request failed (HTTP ${response.status}).`, response.status);
  }
}

// `signal` lets a component cancel a request it no longer needs (e.g. it was unmounted).

export function getCustomers(signal?: AbortSignal): Promise<CustomerDto[]> {
  return request<CustomerDto[]>("/api/customers", { signal });
}

export function getProducts(signal?: AbortSignal): Promise<ProductDto[]> {
  return request<ProductDto[]>("/api/products", { signal });
}

export function getCustomerCards(customerId: number, signal?: AbortSignal): Promise<SavedCardDto[]> {
  return request<SavedCardDto[]>(`/api/customers/${encodeURIComponent(customerId)}/cards`, { signal });
}

export function postCheckout(body: CheckoutRequest, idempotencyKey: string): Promise<CheckoutResult> {
  return request<CheckoutResult>("/api/checkout", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      // The same key for every retry of one purchase attempt: the API then never charges twice.
      "Idempotency-Key": idempotencyKey,
    },
    body: JSON.stringify(body),
  });
}

export function getTransaction(invoiceNo: string, signal?: AbortSignal): Promise<TransactionStatusDto> {
  return request<TransactionStatusDto>(`/api/transactions/${encodeURIComponent(invoiceNo)}`, { signal });
}
