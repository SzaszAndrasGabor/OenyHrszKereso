namespace OenyHrszKereso;

/// <summary>
/// Egy sor az Excel bemeneti fájlból.
/// Az Excel-nek két oszlopa kell legyen: "Varos" és "HRSZ"
/// </summary>
public record InputRow(string Varos, string Hrsz);

/// <summary>
/// Az OÉNY-ből visszanyert találat adatai.
/// </summary>
public record SearchResult
{
    public string Varos { get; init; } = string.Empty;
    public string InputHrsz { get; init; } = string.Empty;

    // Találat adatok
    public string? Cim { get; init; }
    public string? TalalaltHrsz { get; init; }
    public string? TerkeLink { get; init; }
    public string? EovX { get; init; }
    public string? EovY { get; init; }
    public string? Megjegyzes { get; init; }

    // Státusz
    public bool Sikeres { get; init; }
    public string? Hiba { get; init; }
}