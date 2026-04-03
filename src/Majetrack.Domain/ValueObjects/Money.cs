using Majetrack.Domain.Enums;

namespace Majetrack.Domain.ValueObjects;

public readonly record struct Money(decimal Amount, Currency Currency);
