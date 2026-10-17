import { formatMoney } from "@/lib/format";
import type { ProductDto } from "@/lib/types";
import { selectClass } from "./styles";

interface Props {
  products: ProductDto[];
  value: number | null;
  onChange: (productId: number | null) => void;
  disabled?: boolean;
}

export default function ProductSelect({ products, value, onChange, disabled }: Props) {
  return (
    <label className="block text-sm font-medium text-slate-700">
      Product
      <select
        className={selectClass}
        value={value ?? ""}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
      >
        <option value="">Select a product</option>
        {products.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name} ({formatMoney(p.price, p.currency)})
          </option>
        ))}
      </select>
    </label>
  );
}
