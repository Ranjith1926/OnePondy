namespace OnePondy.Domain.Places;

/// <summary>A place category such as "beach". Codes are stable keys the app translates.</summary>
public sealed class PlaceCategory
{
    private PlaceCategory()
    {
    }

    public PlaceCategory(string code, string name, int sortOrder)
    {
        Code = code;
        Name = name;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }
}
