using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace OenyHrszKereso;

public class ExcelService
{
    private readonly ILogger<ExcelService> _logger;

    public ExcelService(ILogger<ExcelService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Beolvassa a bemeneti Excel fájlt.
    /// Az első munkalap első sora fejléc, utána "Varos" és "HRSZ" oszlopok.
    /// </summary>
    public List<InputRow> ReadInput(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"A bemeneti fájl nem található: {filePath}");

        var rows = new List<InputRow>();

        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();

        // Fejléc sor keresése (Varos + HRSZ oszlopok)
        var headerRow = sheet.Row(1);
        int varosCol = -1, hrszCol = -1;

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToLower();
            if (header is "varos" or "város" or "település" or "telepules")
                varosCol = cell.Address.ColumnNumber;
            else if (header is "hrsz" or "helyrajzi szám" or "helyrajzi szam")
                hrszCol = cell.Address.ColumnNumber;
        }

        if (varosCol == -1 || hrszCol == -1)
            throw new InvalidOperationException(
                "Nem találhatók a kötelező oszlopok (Varos, HRSZ) az Excel fájlban. " +
                "Ellenőrizd, hogy az első sor tartalmazza ezeket a fejléceket.");

        // Adatsorok feldolgozása
        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var varos = row.Cell(varosCol).GetString().Trim();
            var hrsz = row.Cell(hrszCol).GetString().Trim();

            if (string.IsNullOrWhiteSpace(varos) || string.IsNullOrWhiteSpace(hrsz))
            {
                _logger.LogWarning("Üres sor a(z) {Row}. sorban, kihagyva.", r);
                continue;
            }

            rows.Add(new InputRow(varos, hrsz));
        }

        _logger.LogInformation("{Count} bemeneti sor beolvasva.", rows.Count);
        return rows;
    }

    /// <summary>
    /// Kiírja az eredményeket egy Excel fájlba.
    /// </summary>
    public void WriteResults(string filePath, List<SearchResult> results)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Eredmenyek");

        // Fejléc
        var headers = new[]
        {
            "Város (bemenet)", "HRSZ (bemenet)",
            "Talált cím", "Talált HRSZ",
            "Térkép link",
            "EOV X", "EOV Y",
            "Megjegyzés", "Státusz", "Hiba"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        // Adatsorok
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            int row = i + 2;

            sheet.Cell(row, 1).Value = r.Varos;
            sheet.Cell(row, 2).Value = r.InputHrsz;
            sheet.Cell(row, 3).Value = r.Cim ?? "";
            sheet.Cell(row, 4).Value = r.TalalaltHrsz ?? "";

            // Térkép link hiperhivatkozásként (5. oszlop)
            if (!string.IsNullOrEmpty(r.TerkeLink))
            {
                var linkCell = sheet.Cell(row, 5);
                linkCell.Value = "Megnyitás";
                linkCell.SetHyperlink(new XLHyperlink(r.TerkeLink));
                linkCell.Style.Font.FontColor = XLColor.Blue;
                linkCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }

            // EOV koordináták (6-7. oszlop)
            sheet.Cell(row, 6).Value = r.EovX ?? "";
            sheet.Cell(row, 7).Value = r.EovY ?? "";

            sheet.Cell(row, 8).Value = r.Megjegyzes ?? "";
            sheet.Cell(row, 9).Value = r.Sikeres ? "✓ Sikeres" : "✗ Hiba";
            sheet.Cell(row, 10).Value = r.Hiba ?? "";

            // Hiba sorok pirossal
            if (!r.Sikeres)
            {
                sheet.Row(row).Cells(1, headers.Length)
                    .Style.Fill.BackgroundColor = XLColor.LightCoral;
            }
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);

        _logger.LogInformation("Eredmények kiírva: {Path}", filePath);
    }
}