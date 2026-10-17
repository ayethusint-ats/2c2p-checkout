import { formatMoney, formatPaymentType } from "@/lib/format";
import type { CustomerDto, ProductDto } from "@/lib/types";

interface Props {
  customer: CustomerDto;
  product: ProductDto;
}

// Only displays what it's given; the amount actually charged is decided by the API from the product.
export default function PaymentSummary({ customer, product }: Props) {
  return (
    <section aria-labelledby="summary-heading" className="rounded-md bg-slate-50 p-4">
      <h2 id="summary-heading" className="text-sm font-semibold text-slate-900">
        Payment summary
      </h2>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-500">Customer</dt>
        <dd className="text-slate-900">
          {customer.firstName} {customer.lastName}
        </dd>
        <dt className="text-slate-500">Product</dt>
        <dd className="text-slate-900">{product.name}</dd>
        <dt className="text-slate-500">Payment type</dt>
        <dd className="text-slate-900">{formatPaymentType(product.paymentType)}</dd>
        <dt className="text-slate-500">Price</dt>
        <dd className="font-semibold text-slate-900">{formatMoney(product.price, product.currency)}</dd>
      </dl>
    </section>
  );
}
