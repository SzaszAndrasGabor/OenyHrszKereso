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
    /// Oszlopok: Varos | HRSZ (opcionális) | Cim (opcionális)
    /// Ha HRSZ üres → cím szerinti keresés
    /// </summary>
    public List<InputRow> ReadInput(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"A bemeneti fájl nem található: {filePath}");

        var rows = new List<InputRow>();
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();

        var headerRow = sheet.Row(1);
        int varosCol = -1, hrszCol = -1, cimCol = -1;

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToLower();
            if (header is "varos" or "város" or "település" or "telepules")
                varosCol = cell.Address.ColumnNumber;
            else if (header is "hrsz" or "helyrajzi szám" or "helyrajzi szam")
                hrszCol = cell.Address.ColumnNumber;
            else if (header is "cim" or "cím" or "utca" or "address")
                cimCol = cell.Address.ColumnNumber;
        }

        if (varosCol == -1)
            throw new InvalidOperationException(
                "Nem található a 'Varos' oszlop. " +
                "Ellenőrizd, hogy az első sor tartalmazza a fejléceket.");

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var varos = varosCol > 0 ? row.Cell(varosCol).GetString().Trim() : "";
            var hrsz = hrszCol > 0 ? row.Cell(hrszCol).GetString().Trim() : "";
            var cim = cimCol > 0 ? row.Cell(cimCol).GetString().Trim() : "";

            if (string.IsNullOrWhiteSpace(varos))
            {
                _logger.LogWarning("Üres város a(z) {Row}. sorban, kihagyva.", r);
                continue;
            }

            if (string.IsNullOrWhiteSpace(hrsz) && string.IsNullOrWhiteSpace(cim))
            {
                _logger.LogWarning("Sem HRSZ, sem cím nincs megadva a(z) {Row}. sorban, kihagyva.", r);
                continue;
            }

            rows.Add(new InputRow(varos, hrsz, cim));
        }

        int hrszDb = rows.Count(r => !string.IsNullOrWhiteSpace(r.Hrsz));
        int cimDb = rows.Count(r => string.IsNullOrWhiteSpace(r.Hrsz));
        _logger.LogInformation("{Total} bemeneti sor beolvasva ({Hrsz} HRSZ, {Cim} cím alapú).",
            rows.Count, hrszDb, cimDb);

        return rows;
    }

    /// <summary>
    /// Kiírja az eredményeket egy Excel fájlba.
    /// </summary>
    public void WriteResults(string filePath, List<SearchResult> results)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Eredmenyek");

        var headers = new[]
        {
            "Város (bemenet)", "HRSZ (bemenet)", "Cím (bemenet)",
            "Keresési mód",
            "Talált cím", "Talált HRSZ",
            "Térkép link",
            "EOV X", "EOV Y",
            "Albetétek (db)",
            "Státusz", "Tulajdoni lap", "Hiba"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        int rowNum = 2;
        string? prevInputKey = null;

        foreach (var r in results)
        {
            var inputKey = $"{r.Varos}|{r.InputHrsz}|{r.InputCim}";
            bool altGroup = inputKey != prevInputKey && prevInputKey != null;
            prevInputKey = inputKey;

            sheet.Cell(rowNum, 1).Value = r.Varos;
            sheet.Cell(rowNum, 2).Value = r.InputHrsz;
            sheet.Cell(rowNum, 3).Value = r.InputCim;
            sheet.Cell(rowNum, 4).Value = r.KeresesiMod;
            sheet.Cell(rowNum, 5).Value = r.Cim ?? "";
            sheet.Cell(rowNum, 6).Value = r.TalalaltHrsz ?? "";

            // Térkép link (7. oszlop)
            if (!string.IsNullOrEmpty(r.TerkeLink))
            {
                var linkCell = sheet.Cell(rowNum, 7);
                linkCell.Value = "Megnyitás";
                linkCell.SetHyperlink(new XLHyperlink(r.TerkeLink));
                linkCell.Style.Font.FontColor = XLColor.Blue;
                linkCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }

            sheet.Cell(rowNum, 8).Value = r.EovX ?? "";
            sheet.Cell(rowNum, 9).Value = r.EovY ?? "";

            if (r.Albetek.HasValue)
                sheet.Cell(rowNum, 10).Value = r.Albetek.Value;

            sheet.Cell(rowNum, 11).Value = r.Sikeres ? "✓ Sikeres" : "✗ Hiba";

            // Tulajdoni lap link (12. oszlop)
            if (!string.IsNullOrEmpty(r.TulajdoniLapLink))
            {
                var tlCell = sheet.Cell(rowNum, 12);
                tlCell.Value = "Tulajdoni lap";
                try
                {
                    var decodedLink = Uri.UnescapeDataString(r.TulajdoniLapLink);
                    tlCell.SetHyperlink(new XLHyperlink(decodedLink));
                }
                catch
                {
                    tlCell.Value = r.TulajdoniLapLink;
                }
                tlCell.Style.Font.FontColor = XLColor.Blue;
                tlCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }

            sheet.Cell(rowNum, 13).Value = r.Hiba ?? "";

            if (!r.Sikeres)
                sheet.Row(rowNum).Cells(1, headers.Length)
                    .Style.Fill.BackgroundColor = XLColor.LightCoral;
            else if (altGroup)
                sheet.Row(rowNum).Cells(1, headers.Length)
                    .Style.Fill.BackgroundColor = XLColor.AliceBlue;

            rowNum++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
        _logger.LogInformation("Eredmények kiírva: {Path} ({Count} sor)", filePath, rowNum - 2);
    }
}