using Checkout.Domain.Enums;

namespace Checkout.Application.Dtos;

public sealed record ProductDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    PaymentType PaymentType);
