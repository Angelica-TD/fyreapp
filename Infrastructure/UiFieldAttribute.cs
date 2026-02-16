using System;

namespace FyreApp.Infrastructure;

[AttributeUsage(AttributeTargets.Property)]
public sealed class UiFieldAttribute : Attribute
{
    public UiFieldAttribute() { }

    /// <summary>Optional grouping (e.g., "Primary contact", "Billing").</summary>
    public string? Group { get; init; }

    /// <summary>Optional order override (lower comes first).</summary>
    public int Order { get; init; } = int.MaxValue;
}
