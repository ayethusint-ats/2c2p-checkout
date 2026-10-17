import Link from "next/link";
import { formatMoney } from "@/lib/format";
import type { TransactionStatusDto } from "@/lib/types";
import { linkButtonClass } from "./styles";

// The three states of a payment on the result page. Each is a small component of its own.

export function ProcessingView() {
  return (
    // role="status" + aria-live: screen readers announce it without moving focus.
    <div role="status" aria-live="polite" className="flex flex-col items-center gap-3 py-6 text-center">
      <span
        aria-hidden="true"
        className="h-10 w-10 animate-spin rounded-full border-4 border-slate-200 border-t-blue-600"
      />
      <p className="font-medium text-slate-900">Processing payment</p>
      <p className="text-sm text-slate-500">This page updates by itself. Please don&apos;t pay again.</p>
    </div>
  );
}

export function SucceededView({ transaction }: { transaction: TransactionStatusDto }) {
  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold text-green-700">Payment succeeded</h2>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-500">Product</dt>
        <dd className="text-slate-900">{transaction.productName}</dd>
        <dt className="text-slate-500">Amount</dt>
        <dd className="font-semibold text-slate-900">
          {formatMoney(transaction.amount, transaction.currencyCode)}
        </dd>
        <dt className="text-slate-500">Invoice</dt>
        <dd className="break-all text-slate-900">{transaction.invoiceNo}</dd>
        <dt className="text-slate-500">Payment method</dt>
        <dd className="text-slate-900">{transaction.paymentChannel ?? "—"}</dd>
        <dt className="text-slate-500">Card / account</dt>
        <dd className="text-slate-900">{transaction.maskedCardNo ?? "—"}</dd>
      </dl>
      <Link href="/" className={linkButtonClass}>
        New checkout
      </Link>
    </div>
  );
}

export function FailedView({ transaction }: { transaction: TransactionStatusDto }) {
  // The ids come from the API's response, not from the URL, so they're values we stored ourselves.
  const backHref = `/?customerId=${transaction.customerId}&productId=${transaction.productId}`;

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold text-red-700">Payment failed</h2>
      <p className="text-sm text-slate-700">{transaction.respDesc ?? "The payment was not completed."}</p>
      <Link href={backHref} className={linkButtonClass}>
        Go back
      </Link>
    </div>
  );
}
