using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace FyreApp.Infrastructure;

public sealed record UiField(string Label, string Value, string? Group, int Order);

public static class UiFieldBuilder
{
    public static IReadOnlyList<UiField> Build<T>(T model) where T : class
    {
        if (model is null) return Array.Empty<UiField>();

        var type = model.GetType();

        var fields = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => new { Prop = p, Ui = p.GetCustomAttribute<UiFieldAttribute>() })
            .Where(x => x.Ui is not null) // only explicitly allowed
            .Where(x => x.Prop.GetIndexParameters().Length == 0) // no indexers
            .Where(x => IsSimpleType(x.Prop.PropertyType)) // avoid nav props/collections
            .Select(x =>
            {
                var label = GetLabel(x.Prop);
                var valueObj = x.Prop.GetValue(model);
                var value = FormatValue(valueObj);

                var orderFromUi = x.Ui!.Order;

                var order = orderFromUi != int.MaxValue
                    ? orderFromUi
                    : x.Prop.GetCustomAttribute<DisplayAttribute>()?.GetOrder() ?? int.MaxValue;

                return new UiField(label, value, x.Ui.Group, order);
            })
            .OrderBy(f => f.Group ?? string.Empty)
            .ThenBy(f => f.Order)
            .ThenBy(f => f.Label)
            .ToList();

        return fields;
    }

    private static string GetLabel(PropertyInfo prop)
    {
        var display = prop.GetCustomAttribute<DisplayAttribute>();
        if (!string.IsNullOrWhiteSpace(display?.Name))
            return display!.Name!;

        var displayName = prop.GetCustomAttribute<DisplayNameAttribute>();
        if (!string.IsNullOrWhiteSpace(displayName?.DisplayName))
            return displayName!.DisplayName!;

        // Fallback: "PrimaryContactName" -> "Primary Contact Name"
        return SplitPascalCase(prop.Name);
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "—";

        return value switch
        {
            DateTime dt => dt.ToString("d", CultureInfo.CurrentCulture),
            DateTimeOffset dto => dto.ToString("d", CultureInfo.CurrentCulture),
            bool b => b ? "Yes" : "No",
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? "—"
        };
    }

    private static bool IsSimpleType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;

        if (t.IsEnum) return true;

        return t.IsPrimitive
               || t == typeof(string)
               || t == typeof(decimal)
               || t == typeof(DateTime)
               || t == typeof(DateTimeOffset)
               || t == typeof(Guid);
    }

    private static string SplitPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var chars = input.ToCharArray();
        var result = new List<char>(chars.Length + 8);

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var prev = i > 0 ? chars[i - 1] : '\0';
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            var boundary =
                i > 0 &&
                char.IsUpper(c) &&
                (char.IsLower(prev) || (char.IsUpper(prev) && char.IsLower(next)));

            if (boundary) result.Add(' ');
            result.Add(c);
        }

        return new string(result.ToArray());
    }
}
