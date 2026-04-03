using Majetrack.Domain.Enums;

namespace Majetrack.Domain.ValueObjects;

/// <summary>
/// An immutable value object representing a monetary amount paired with its currency.
/// Ensures that financial calculations always carry currency context, preventing
/// accidental mixing of different currencies in arithmetic operations.
/// </summary>
/// <param name="Amount">The monetary value. Precision: 18,2.</param>
/// <param name="Currency">The currency in which the amount is denominated.</param>
public readonly record struct Money(decimal Amount, Currency Currency);
