using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OenyHrszKereso;

// ── Konfiguráció ──────────────────────────────────────────────────────────────
var baseDir = AppContext.BaseDirectory;
var projectDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));
var inputFile = args.Length > 0 ? args[0] : Path.Combine(projectDir, "bemenet.xlsx");
var outputFile = args.Length > 1 ? args[1] : Path.Combine(projectDir, "eredmenyek.xlsx");

var config = new ScraperConfig
{
    Headless                = false,   // false = látható böngésző (debughoz)
    SlowMo                  = 500,
    DelayBetweenSearchesMs  = 1500,   // 1.5 mp keresések közt — szerver kímélése
    NavigationTimeoutMs     = 30_000,
    ElementTimeoutMs        = 10_000,
};

// ── DI konténer ───────────────────────────────────────────────────────────────
var services = new ServiceCollection()
    .AddLogging(b => b
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information))
    .AddSingleton(config)
    .AddSingleton<ExcelService>()
    .AddSingleton<OenyScraperService>()
    .BuildServiceProvider();

var logger      = services.GetRequiredService<ILogger<Program>>();
var excel       = services.GetRequiredService<ExcelService>();
var scraper     = services.GetRequiredService<OenyScraperService>();

// ── Fő folyamat ───────────────────────────────────────────────────────────────
logger.LogInformation("OÉNY HRSZ Kereső indítása");
logger.LogInformation("Bemenet : {Input}",  inputFile);
logger.LogInformation("Kimenet : {Output}", outputFile);

try
{
    // 1. Excel beolvasás
    var rows = excel.ReadInput(inputFile);
    if (rows.Count == 0)
    {
        logger.LogWarning("Nincs feldolgozandó sor. Kilépés.");
        return 1;
    }

    // 2. Playwright inicializálás
    // Első futáskor: `playwright install chromium` szükséges!
    await scraper.InitAsync();

    // 3. Keresések futtatása
    var results = new List<SearchResult>();
    int idx = 0;
    foreach (var row in rows)
    {
        idx++;
        logger.LogInformation("[{Idx}/{Total}] {Varos} / {Hrsz}",
            idx, rows.Count, row.Varos, row.Hrsz);

        var result = await scraper.SearchAsync(row);
        results.Add(result);

        if (result.Sikeres)
            logger.LogInformation("  ✓ Cím: {Cim}  |  HRSZ: {Hrsz}", result.Cim, result.TalalaltHrsz);
        else
            logger.LogWarning("  ✗ Hiba: {Hiba}", result.Hiba);

        // Keresések közti szünet (szerver kímélése)
        if (idx < rows.Count)
            await Task.Delay(config.DelayBetweenSearchesMs);
    }

    // 4. Eredmények kiírása
    excel.WriteResults(outputFile, results);

    int sikeresDb = results.Count(r => r.Sikeres);
    logger.LogInformation("Kész! {Ok}/{Total} sikeres keresés. Kimenet: {Output}",
        sikeresDb, results.Count, outputFile);

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
