namespace Checkout.Application.Dtos;

/// <summary>
/// What the UI needs to show a saved card. Deliberately omits customer_token
/// (it can charge the card at 2C2P) and customer_id (already in the URL).
/// </summary>
public sealed record SavedCardDto(int Id, string MaskedCardNo);
