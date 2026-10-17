"use client";

// "use client": this page runs in the browser. It needs state, effects and click handlers,
// which only exist there. (Without it, Next.js renders a component on the server only.)

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Alert from "@/components/Alert";
import CustomerSelect from "@/components/CustomerSelect";
import PaymentSummary from "@/components/PaymentSummary";
import ProductSelect from "@/components/ProductSelect";
import SavedCardOptions from "@/components/SavedCardOptions";
import { primaryButtonClass } from "@/components/styles";
import { ApiError, getCustomerCards, getCustomers, getProducts, postCheckout } from "@/lib/api";
import type { CustomerDto, ProductDto, SavedCardDto } from "@/lib/types";

// Next.js requires a Suspense boundary around anything that reads the query string
// (useSearchParams), because the query string is only known in the browser.
export default function CheckoutPage() {
  return (
    <Suspense fallback={<p className="text-sm text-slate-500">Loading…</p>}>
      <CheckoutForm />
    </Suspense>
  );
}

function CheckoutForm() {
  const router = useRouter();
  const searchParams = useSearchParams();

  // useState: a value React remembers between renders; calling the setter re-renders the page.
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [productId, setProductId] = useState<number | null>(null);
  const [cards, setCards] = useState<SavedCardDto[]>([]);
  // null = "Pay with a new payment method" (the default).
  const [selectedCardId, setSelectedCardId] = useState<number | null>(null);
  // One key per purchase attempt (see the handlers below for when it changes).
  // The function form runs once, when the page loads, not on every render.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // useEffect: run code after the page renders. Dependencies [searchParams]: run on load
  // (and again only if the query string changes). The returned function is the cleanup.
  useEffect(() => {
    const controller = new AbortController();

    Promise.all([getCustomers(controller.signal), getProducts(controller.signal)])
      .then(([loadedCustomers, loadedProducts]) => {
        setCustomers(loadedCustomers);
        setProducts(loadedProducts);

        // Optional preselection from the URL:
        //   /?customerId=2&productId=1  ("Go back" after a failure) -> those two preselected
        //   /                           ("New checkout")            -> an empty form
        // Values that aren't in the loaded lists are ignored.
        const wantedCustomer = Number(searchParams.get("customerId"));
        if (loadedCustomers.some((c) => c.id === wantedCustomer)) setCustomerId(wantedCustomer);
        const wantedProduct = Number(searchParams.get("productId"));
        if (loadedProducts.some((p) => p.id === wantedProduct)) setProductId(wantedProduct);
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) setError(messageFor(err));
      });

    // Cancels the requests if the page goes away before they finish.
    return () => controller.abort();
  }, [searchParams]);

  // The browser's Back button can restore this page from its back/forward cache, exactly as it was
  // ("Processing…"), without re-running any effect. `pageshow` with persisted = true is that case.
  useEffect(() => {
    function handlePageShow(event: PageTransitionEvent) {
      if (event.persisted) setSubmitting(false);
    }
    window.addEventListener("pageshow", handlePageShow);
    return () => window.removeEventListener("pageshow", handlePageShow);
  }, []);

  // Load the selected customer's saved cards whenever the customer changes.
  useEffect(() => {
    if (customerId === null) return;
    const controller = new AbortController();

    getCustomerCards(customerId, controller.signal)
      .then(setCards)
      .catch((err: unknown) => {
        if (!controller.signal.aborted) setError(messageFor(err));
      });

    // A quick second change cancels the first request, so old cards can't overwrite new ones.
    return () => controller.abort();
  }, [customerId]);

  // A different customer, product or payment method is a different purchase: it gets a new key.
  // Reusing the old key would make the API answer 422 (key reused for a different purchase).
  function handleCustomerChange(id: number | null) {
    setCustomerId(id);
    setCards([]);
    setSelectedCardId(null);
    setIdempotencyKey(crypto.randomUUID());
    setError(null);
  }

  function handleProductChange(id: number | null) {
    setProductId(id);
    setIdempotencyKey(crypto.randomUUID());
    setError(null);
  }

  function handlePaymentMethodChange(cardId: number | null) {
    setSelectedCardId(cardId);
    setIdempotencyKey(crypto.randomUUID());
    setError(null);
  }

  async function handleCheckout() {
    if (customerId === null || productId === null) return;
    setSubmitting(true);
    setError(null);

    try {
      const result = await postCheckout({ customerId, productId, customerCardId: selectedCardId }, idempotencyKey);

      // A failed attempt is finished: trying again is a new attempt, so it needs a new key.
      if (result.status === "Failed") setIdempotencyKey(crypto.randomUUID());

      // The 2C2P payment page or a 3-D Secure page. Only an absolute https URL is followed.
      if (result.redirectUrl && isAbsoluteHttpsUrl(result.redirectUrl)) {
        window.location.assign(result.redirectUrl);
        return;
      }

      // Saved card without 3-D Secure, or Failed: the result page shows the outcome.
      router.push(`/result?invoiceNo=${encodeURIComponent(result.invoiceNo)}`);
      // `submitting` stays true while we navigate away, so the button can't be pressed again.
    } catch (err: unknown) {
      // Network error or 409: the SAME attempt may still be running, so the key is kept.
      // Pressing Checkout again then returns that attempt's result instead of paying twice.
      setError(messageFor(err));
      setSubmitting(false);
    }
  }

  // Derived values: computed on every render from state, so they can never go stale.
  const customer = customers.find((c) => c.id === customerId);
  const product = products.find((p) => p.id === productId);
  const canCheckout = customer !== undefined && product !== undefined && !submitting;

  return (
    <div className="space-y-5">
      <h1 className="text-xl font-semibold">Checkout</h1>

      <CustomerSelect customers={customers} value={customerId} onChange={handleCustomerChange} disabled={submitting} />
      <ProductSelect products={products} value={productId} onChange={handleProductChange} disabled={submitting} />

      {customer && product && <PaymentSummary customer={customer} product={product} />}

      {customer && (
        <SavedCardOptions
          cards={cards}
          selectedCardId={selectedCardId}
          onChange={handlePaymentMethodChange}
          disabled={submitting}
        />
      )}

      {error && <Alert message={error} />}

      <button type="button" className={primaryButtonClass} disabled={!canCheckout} onClick={handleCheckout}>
        {submitting ? "Processing…" : "Checkout"}
      </button>
    </div>
  );
}

function isAbsoluteHttpsUrl(value: string): boolean {
  try {
    return new URL(value).protocol === "https:";
  } catch {
    return false; // not an absolute URL at all
  }
}

// Turns any error into a message for the user. Never raw JSON or a stack trace.
function messageFor(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 409) return "Your payment is still being processed. Please try again in a moment.";
    return err.message;
  }
  // fetch rejects with a TypeError when the server can't be reached at all.
  if (err instanceof TypeError) return "Could not reach the payment server. Check your connection and try again.";
  return "Something went wrong. Please try again.";
}
