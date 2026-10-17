"use client";

// The folder decides the route: src/app/result/page.tsx is served at /result.

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { FailedView, ProcessingView, SucceededView } from "@/components/StatusView";
import { linkButtonClass, primaryButtonClass } from "@/components/styles";
import { ApiError, getTransaction } from "@/lib/api";
import type { TransactionStatusDto } from "@/lib/types";

const PollIntervalMs = 3_000;
const MaxPollingMs = 2 * 60_000;

// What the page is showing. A union type: exactly one of these at a time.
type View =
  | { kind: "loading" }
  | { kind: "final"; transaction: TransactionStatusDto }
  | { kind: "timedOut" }
  | { kind: "notFound" }
  | { kind: "error" };

export default function ResultPage() {
  return (
    <Suspense fallback={<ProcessingView />}>
      <PaymentResult />
    </Suspense>
  );
}

// Only the leading letters and digits: 2C2P may append its own parameters to our return URL.
function sanitizeInvoiceNo(raw: string | null): string | null {
  return /^[A-Za-z0-9]+/.exec(raw ?? "")?.[0] ?? null;
}

function PaymentResult() {
  const searchParams = useSearchParams();
  // invoiceNo is the ONLY thing read from the URL. Every other query parameter (including anything
  // 2C2P adds) is ignored: what we display comes from our API, so the URL can't fake a result.
  const invoiceNo = sanitizeInvoiceNo(searchParams.get("invoiceNo"));

  const [view, setView] = useState<View>({ kind: "loading" });
  // Bumping this number re-runs the polling effect ("Check again" / "Try again").
  const [pollRound, setPollRound] = useState(0);

  useEffect(() => {
    if (invoiceNo === null) return;

    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const startedAt = Date.now();

    // The next poll is scheduled only after this one finishes, so requests never overlap.
    async function poll(invoice: string) {
      try {
        const transaction = await getTransaction(invoice, controller.signal);

        if (transaction.status !== "Pending") {
          setView({ kind: "final", transaction });
          return; // final: stop polling
        }
        if (Date.now() - startedAt >= MaxPollingMs) {
          setView({ kind: "timedOut" });
          return; // give up for now; the user can check again
        }

        setView({ kind: "loading" });
        timer = setTimeout(() => void poll(invoice), PollIntervalMs);
      } catch (err: unknown) {
        if (controller.signal.aborted) return; // we cancelled it ourselves (cleanup below)
        setView(err instanceof ApiError && err.status === 404 ? { kind: "notFound" } : { kind: "error" });
      }
    }

    void poll(invoiceNo);

    // Cleanup runs when the page unmounts (or before the effect re-runs): without it, a timer
    // would keep polling, and a late response would update a page that is no longer shown.
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [invoiceNo, pollRound]);

  function pollAgain() {
    setView({ kind: "loading" });
    setPollRound((round) => round + 1);
  }

  if (invoiceNo === null) return <NotFound />;

  switch (view.kind) {
    case "loading":
      return <ProcessingView />;
    case "final":
      return view.transaction.status === "Succeeded" ? (
        <SucceededView transaction={view.transaction} />
      ) : (
        <FailedView transaction={view.transaction} />
      );
    case "timedOut":
      return <Retry message="Still processing. Check again in a moment." buttonText="Check again" onRetry={pollAgain} />;
    case "notFound":
      return <NotFound />;
    case "error":
      return (
        <Retry message="We couldn't load the payment status." buttonText="Try again" onRetry={pollAgain} />
      );
  }
}

function NotFound() {
  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Transaction not found</h1>
      <Link href="/" className={linkButtonClass}>
        Back to checkout
      </Link>
    </div>
  );
}

function Retry({ message, buttonText, onRetry }: { message: string; buttonText: string; onRetry: () => void }) {
  return (
    <div className="space-y-4">
      <p role="status" className="text-slate-700">
        {message}
      </p>
      <button type="button" className={primaryButtonClass} onClick={onRetry}>
        {buttonText}
      </button>
    </div>
  );
}
