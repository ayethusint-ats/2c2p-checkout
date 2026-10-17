import type { SavedCardDto } from "@/lib/types";

interface Props {
  cards: SavedCardDto[];
  // null means "Pay with a new payment method".
  selectedCardId: number | null;
  onChange: (cardId: number | null) => void;
  disabled?: boolean;
}

const radioClass = "h-4 w-4 accent-blue-600 focus-visible:outline-2 focus-visible:outline-blue-600";

export default function SavedCardOptions({ cards, selectedCardId, onChange, disabled }: Props) {
  return (
    <fieldset disabled={disabled}>
      <legend className="text-sm font-medium text-slate-700">Payment method</legend>
      <div className="mt-2 space-y-2">
        {/* The card list only appears when the customer has saved cards. */}
        {cards.map((card) => (
          <label key={card.id} className="flex items-center gap-2 text-sm text-slate-900">
            <input
              type="radio"
              name="payment-method"
              className={radioClass}
              checked={selectedCardId === card.id}
              onChange={() => onChange(card.id)}
            />
            Saved card {card.maskedCardNo}
          </label>
        ))}
        <label className="flex items-center gap-2 text-sm text-slate-900">
          <input
            type="radio"
            name="payment-method"
            className={radioClass}
            checked={selectedCardId === null}
            onChange={() => onChange(null)}
          />
          Pay with a new payment method
        </label>
      </div>
    </fieldset>
  );
}
