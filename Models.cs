namespace OenyHrszKereso;

/// <summary>
/// Egy sor az Excel bemeneti fájlból.
/// Város + HRSZ → HRSZ szerinti keresés
/// Város + Cím  → Cím szerinti keresés (ha HRSZ üres)
/// </summary>
public record InputRow(string Varos, string Hrsz, string Cim);

/// <summary>
/// Egy találati kártya adatai.
/// </summary>
public record SearchResult
{
    public string Varos { get; init; } = string.Empty;
    public string InputHrsz { get; init; } = string.Empty;
    public string InputCim { get; init; } = string.Empty;
    public string KeresesiMod { get; init; } = string.Empty; // "HRSZ" vagy "Cím"

    // Találat adatok
    public string? Cim { get; init; }
    public string? TalalaltHrsz { get; init; }
    public string? TerkeLink { get; init; }
    public string? EovX { get; init; }
    public string? EovY { get; init; }
    public int? Albetek { get; init; }
    public string? TulajdoniLapLink { get; init; }
    public string? Megjegyzes { get; init; }

    // Státusz
    public bool Sikeres { get; init; }
    public string? Hiba { get; init; }
}