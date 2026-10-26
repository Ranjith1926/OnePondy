namespace OnePondy.Application.Common;

/// <summary>Collects form validation problems per field.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
    }

    /// <summary>Checks an optional text value's length; returns the trimmed value or null when blank.</summary>
    public string? Optional(string field, string? value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is { Length: var length } && length > maxLength)
        {
            Add(field, $"Must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    /// <summary>Checks a required text value; returns the trimmed value (empty when missing).</summary>
    public string Required(string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            Add(field, "Required.");
        }
        else if (trimmed.Length > maxLength)
        {
            Add(field, $"Must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public AppError ToError() =>
        AppError.Validation(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}
