using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OenyHrszKereso;

// ── Fájlválasztó ablak ────────────────────────────────────────────────────────
string? inputFile = null;

if (OperatingSystem.IsWindows())
    inputFile = ShowOpenFileDialog();
else
    inputFile = args.Length > 0 ? args[0] : "bemenet.xlsx";

if (string.IsNullOrEmpty(inputFile))
{
    Console.WriteLine("Nem választottál fájlt. Kilépés.");
    return 1;
}

var inputDir = Path.GetDirectoryName(inputFile) ?? ".";
var inputName = Path.GetFileNameWithoutExtension(inputFile);
var outputFile = Path.Combine(inputDir, $"{inputName}_eredmenyek.xlsx");

// ── Playwright telepítő mód: OenyHrszKereso.exe install chromium ─────────────
if (args.Length > 0 && args[0] == "install")
{
    var exitCode = Microsoft.Playwright.Program.Main(args);
    return exitCode;
}

// ── Chrome ellenőrzés ─────────────────────────────────────────────────────────
var chromePaths = new[]
{
    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Google\Chrome\Application\chrome.exe"),
};
var chromeExe = chromePaths.FirstOrDefault(File.Exists);
if (chromeExe != null)
    Console.WriteLine($"Chrome megtalálva: {chromeExe}");
else
    Console.WriteLine("Chrome nem található — beépített Chromium lesz használva. Futtasd egyszer: OenyHrszKereso.exe install chromium");

// ── Konfiguráció ──────────────────────────────────────────────────────────────
var config = new ScraperConfig
{
    Headless = false,
    SlowMo = 50,
    DelayBetweenSearchesMs = 1000,
    NavigationTimeoutMs = 30_000,
    ElementTimeoutMs = 10_000,
};

// ── DI konténer ───────────────────────────────────────────────────────────────
var services = new ServiceCollection()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddSingleton(config)
    .AddSingleton<ExcelService>()
    .AddSingleton<OenyScraperService>()
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILogger<Program>>();
var excel = services.GetRequiredService<ExcelService>();
var scraper = services.GetRequiredService<OenyScraperService>();

// ── Fő folyamat ───────────────────────────────────────────────────────────────
logger.LogInformation("OÉNY HRSZ Kereső indítása");
logger.LogInformation("Bemenet : {Input}", inputFile);
logger.LogInformation("Kimenet : {Output}", outputFile);

try
{
    var rows = excel.ReadInput(inputFile);
    if (rows.Count == 0)
    {
        logger.LogWarning("Nincs feldolgozandó sor. Kilépés.");
        return 1;
    }

    await scraper.InitAsync();

    var allResults = new List<SearchResult>();
    int idx = 0;

    foreach (var row in rows)
    {
        idx++;
        logger.LogInformation("[{Idx}/{Total}] {Varos} / {Hrsz}",
            idx, rows.Count, row.Varos, row.Hrsz);

        // SearchAsync most listát ad vissza (több kártya = több sor)
        var cardResults = await scraper.SearchAsync(row);
        allResults.AddRange(cardResults);

        int sikeresKartyak = cardResults.Count(r => r.Sikeres);
        if (sikeresKartyak > 0)
            logger.LogInformation("  ✓ {Count} találati kártya", sikeresKartyak);
        else
            logger.LogWarning("  ✗ Hiba: {Hiba}", cardResults.FirstOrDefault()?.Hiba);

        if (idx < rows.Count)
            await Task.Delay(config.DelayBetweenSearchesMs);
    }

    excel.WriteResults(outputFile, allResults);

    int sikeresDb = allResults.Count(r => r.Sikeres);
    logger.LogInformation("Kész! {Ok}/{Total} sikeres találat, {Rows} Excel sor. Kimenet: {Output}",
        sikeresDb, allResults.Count, allResults.Count, outputFile);

    // Eredményfájl automatikus megnyitása
    if (OperatingSystem.IsWindows())
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = outputFile,
            UseShellExecute = true
        });
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Váratlan hiba");
    return 2;
}
finally
{
    await scraper.DisposeAsync();
}

// ── Windows fájlválasztó ablak ────────────────────────────────────────────────
static string? ShowOpenFileDialog()
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"" +
                "Add-Type -AssemblyName System.Windows.Forms; " +
                "$dlg = New-Object System.Windows.Forms.OpenFileDialog; " +
                "$dlg.Title = 'Válaszd ki a bemeneti Excel fájlt'; " +
                "$dlg.Filter = 'Excel fájlok (*.xlsx)|*.xlsx|Minden fájl (*.*)|*.*'; " +
                "$dlg.FilterIndex = 1; " +
                "if ($dlg.ShowDialog() -eq 'OK') { Write-Output $dlg.FileName } " +
                "else { Write-Output '' }\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        var result = process?.StandardOutput.ReadToEnd().Trim();
        process?.WaitForExit();

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Fájlválasztó hiba: {ex.Message}");
        Console.Write("Add meg a fájl elérési útját kézzel: ");
        return Console.ReadLine()?.Trim();
    }
}