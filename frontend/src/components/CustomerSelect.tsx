import type { CustomerDto } from "@/lib/types";
import { selectClass } from "./styles";

interface Props {
  customers: CustomerDto[];
  value: number | null;
  onChange: (customerId: number | null) => void;
  disabled?: boolean;
}

// A "controlled" select: the parent owns the selected id and passes it in as `value`;
// a change is reported back through `onChange` instead of the select keeping its own state.
export default function CustomerSelect({ customers, value, onChange, disabled }: Props) {
  return (
    <label className="block text-sm font-medium text-slate-700">
      Customer
      <select
        className={selectClass}
        value={value ?? ""}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
      >
        <option value="">Select a customer</option>
        {customers.map((c) => (
          // `key` lets React match list items between renders; it must be unique and stable.
          <option key={c.id} value={c.id}>
            {c.firstName} {c.lastName}
          </option>
        ))}
      </select>
    </label>
  );
}
