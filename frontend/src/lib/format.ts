import type { PaymentType } from "./types";

/** e.g. 89.9 + "SGD" -> "SGD 89.90". The currency always comes from the data, never assumed. */
export function formatMoney(amount: number, currencyCode: string): string {
  try {
    return new Intl.NumberFormat("en-SG", {
      style: "currency",
      currency: currencyCode,
      currencyDisplay: "code",
    }).format(amount);
  } catch {
    // Intl throws on an unknown currency code; still show something readable.
    return `${currencyCode} ${amount.toFixed(2)}`;
  }
}

export function formatPaymentType(paymentType: PaymentType): string {
  return paymentType === "Recurring" ? "Recurring (card saved for later payments)" : "One-time";
}
