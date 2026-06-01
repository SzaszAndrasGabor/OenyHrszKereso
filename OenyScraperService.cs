using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace OenyHrszKereso;

public class OenyScraperService : IAsyncDisposable
{
    private readonly ILogger<OenyScraperService> _logger;
    private readonly ScraperConfig _config;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    // Az API válaszokból kinyert koordináták ideiglenes tárolása
    private string? _lastEovX;
    private string? _lastEovY;

    private const string BaseUrl = "https://www.oeny.hu/oeny/hrsz-kereso/";

    public OenyScraperService(ILogger<OenyScraperService> logger, ScraperConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task InitAsync()
    {
        _logger.LogInformation("Playwright inicializálása...");
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _config.Headless,
            SlowMo = _config.SlowMo,
        });

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "hu-HU",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            IgnoreHTTPSErrors = true
        });

        _page = await context.NewPageAsync();
        _page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        // Hálózati válaszok figyelése — koordináták és API struktúra kinyeréséhez
        _page.Response += async (_, response) =>
        {
            try
            {
                var url = response.Url;
                if (url.Contains("/api/") || url.Contains("parcel") || url.Contains("geometry"))
                {
                    var status = response.Status;
                    var body = await response.TextAsync();
                    _logger.LogInformation("API válasz [{Status}] [{Url}]: {Body}",
                        status, url, body[..Math.Min(800, body.Length)]);

                    // EOV koordináta keresése a válaszban
                    if (body.Contains("eov") || body.Contains("EOV") ||
                        body.Contains("geometry") || body.Contains("coordinates") ||
                        body.Contains("centroid") || body.Contains("x") && body.Contains("y"))
                    {
                        TryExtractEovFromJson(body);
                    }
                }
            }
            catch { }
        };

        _logger.LogInformation("Böngésző elindítva. Kezdeti oldalbetöltés...");
        await _page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = _config.NavigationTimeoutMs
        });

        await AcceptCookiesIfNeededAsync();
    }

    private void TryExtractEovFromJson(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Próbálunk különböző mezőneveket
            var xNames = new[] { "eovX", "eov_x", "x", "coordX", "centroidX" };
            var yNames = new[] { "eovY", "eov_y", "y", "coordY", "centroidY" };

            foreach (var xName in xNames)
            {
                if (TryGetJsonValue(root, xName, out var xVal))
                {
                    _lastEovX = xVal;
                    _logger.LogInformation("EOV X koordináta megtalálva ({Field}): {Val}", xName, xVal);
                    break;
                }
            }

            foreach (var yName in yNames)
            {
                if (TryGetJsonValue(root, yName, out var yVal))
                {
                    _lastEovY = yVal;
                    _logger.LogInformation("EOV Y koordináta megtalálva ({Field}): {Val}", yName, yVal);
                    break;
                }
            }
        }
        catch { }
    }

    private static bool TryGetJsonValue(System.Text.Json.JsonElement element, string key, out string? value)
    {
        value = null;
        try
        {
            if (element.TryGetProperty(key, out var prop))
            {
                value = prop.ToString();
                return true;
            }
            // Rekurzív keresés egy szinttel mélyebben
            foreach (var child in element.EnumerateObject())
            {
                if (child.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (TryGetJsonValue(child.Value, key, out value))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private async Task AcceptCookiesIfNeededAsync()
    {
        try
        {
            var cookieSelectors = new[]
            {
                "button:has-text('Elfogadom')",
                "button:has-text('Elfogad')",
                "button:has-text('Engedélyez')",
                "#cookie-accept",
                ".cookie-accept-btn"
            };

            foreach (var selector in cookieSelectors)
            {
                var btn = _page!.Locator(selector).First;
                if (await btn.IsVisibleAsync())
                {
                    await btn.ClickAsync();
                    _logger.LogInformation("Cookie banner elfogadva.");
                    await _page.WaitForTimeoutAsync(500);
                    break;
                }
            }
        }
        catch
        {
            // Cookie banner nem volt, folytatás
        }
    }

    public async Task<SearchResult> SearchAsync(InputRow input)
    {
        _logger.LogInformation("Keresés: {Varos} / {Hrsz}", input.Varos, input.Hrsz);

        // Koordináták törlése az előző keresésből
        _lastEovX = null;
        _lastEovY = null;

        try
        {
            await _page!.GotoAsync(BaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = _config.NavigationTimeoutMs
            });

            // ── 1. LÉPÉS: Település mező kitöltése ───────────────────────────────
            var settlementInput = _page.Locator("input.p-autocomplete-input").Nth(0);
            await settlementInput.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _config.ElementTimeoutMs
            });
            await settlementInput.ClickAsync();
            await settlementInput.FillAsync(input.Varos);
            await _page.WaitForTimeoutAsync(1000);

            // Billentyűzettel választjuk ki az első találatot
            await settlementInput.PressAsync("ArrowDown");
            await _page.WaitForTimeoutAsync(300);
            await settlementInput.PressAsync("Enter");
            await _page.WaitForTimeoutAsync(800);

            // Ha még mindig látható a dropdown, JS kattintással
            try
            {
                var dropdownVisible = await _page.Locator("li.p-autocomplete-item").First.IsVisibleAsync();
                if (dropdownVisible)
                {
                    await _page.Locator("li.p-autocomplete-item").First.EvaluateAsync("el => el.click()");
                    await _page.WaitForTimeoutAsync(800);
                }
            }
            catch { }

            _logger.LogInformation("Település kiválasztva: {Varos}", input.Varos);

            // ── 2. LÉPÉS: "Helyrajzi szám" keresési mód kiválasztása ─────────────
            await _page.WaitForTimeoutAsync(800);
            var hrszTab = _page.Locator(
                "label:has-text('Helyrajzi'), " +
                "button:has-text('Helyrajzi'), " +
                ".p-button:has-text('Helyrajzi'), " +
                "[role='tab']:has-text('Helyrajzi'), " +
                "span:has-text('Helyrajzi szám')"
            ).First;
            await hrszTab.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _config.ElementTimeoutMs
            });
            await hrszTab.ClickAsync();
            _logger.LogInformation("'Helyrajzi szám' mód kiválasztva.");
            await _page.WaitForTimeoutAsync(500);

            // ── 3. LÉPÉS: HRSZ mező kitöltése ────────────────────────────────────
            var hrszInput = _page.Locator("input.p-autocomplete-input").Nth(1);
            await hrszInput.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _config.ElementTimeoutMs
            });
            await hrszInput.ClickAsync();
            await hrszInput.FillAsync(input.Hrsz);
            await _page.WaitForTimeoutAsync(1000);

            // Billentyűzettel választjuk ki az első találatot
            await hrszInput.PressAsync("ArrowDown");
            await _page.WaitForTimeoutAsync(300);
            await hrszInput.PressAsync("Enter");
            await _page.WaitForTimeoutAsync(800);

            // Ha még mindig látható a dropdown, JS kattintással
            try
            {
                var hrszDropdownVisible = await _page.Locator("li.p-autocomplete-item").First.IsVisibleAsync();
                if (hrszDropdownVisible)
                {
                    await _page.Locator("li.p-autocomplete-item").First.EvaluateAsync("el => el.click()");
                    await _page.WaitForTimeoutAsync(800);
                }
            }
            catch { }

            _logger.LogInformation("HRSZ kiválasztva: {Hrsz}", input.Hrsz);

            // ── 4. LÉPÉS: Megvárjuk az eredményt és az API válaszokat ─────────────
            // SPA nem tölt új oldalt, az URL JavaScript-tel változik
            await _page.WaitForTimeoutAsync(2500);

            return await ExtractResultAsync(input);
        }
        catch (TimeoutException tex)
        {
            _logger.LogError("Időtúllépés: {Varos}/{Hrsz} — {Msg}", input.Varos, input.Hrsz, tex.Message);
            return new SearchResult
            {
                Varos = input.Varos,
                InputHrsz = input.Hrsz,
                Sikeres = false,
                Hiba = $"Időtúllépés: {tex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba: {Varos}/{Hrsz}", input.Varos, input.Hrsz);
            return new SearchResult
            {
                Varos = input.Varos,
                InputHrsz = input.Hrsz,
                Sikeres = false,
                Hiba = ex.Message
            };
        }
    }

    private async Task<SearchResult> ExtractResultAsync(InputRow input)
    {
        var currentUrl = _page!.Url;

        // Helyrajzi szám kinyerése az URL state paraméteréből
        string? talalaltHrsz = ExtractHrszFromUrl(currentUrl);

        // Ha az URL-ből nem sikerült, próbáljuk a DOM-ból
        if (string.IsNullOrEmpty(talalaltHrsz))
        {
            talalaltHrsz = await TryGetTextAsync(
                "[class*='lotNumber'], [class*='lot-number'], " +
                ".p-panel-content span:has-text('/')"
            );
        }

        // Cím kinyerése a DOM-ból
        string? cim = await TryGetTextAsync(
            ".p-panel-content span:has-text('utca'), " +
            ".p-panel-content span:has-text('út'), " +
            ".p-panel-content span:has-text('tér'), " +
            "[class*='address'], .address"
        );

        bool sikeres = currentUrl != BaseUrl && currentUrl.Contains("state=");

        return new SearchResult
        {
            Varos = input.Varos,
            InputHrsz = input.Hrsz,
            Cim = cim,
            TalalaltHrsz = talalaltHrsz ?? input.Hrsz,
            TerkeLink = sikeres ? currentUrl : null,
            EovX = _lastEovX,
            EovY = _lastEovY,
            Sikeres = sikeres,
            Hiba = sikeres ? null : "Nem található találat"
        };
    }

    private async Task<string?> TryGetTextAsync(string selector)
    {
        try
        {
            var locator = _page!.Locator(selector).First;
            if (await locator.IsVisibleAsync())
                return (await locator.InnerTextAsync()).Trim();
        }
        catch { }
        return null;
    }

    private static string? ExtractHrszFromUrl(string url)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
            var stateParam = query["state"];
            if (stateParam is null) return null;

            var jsonBytes = Convert.FromBase64String(stateParam + "==");
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            var decoded = System.Web.HttpUtility.UrlDecode(json);

            var doc = System.Text.Json.JsonDocument.Parse(decoded);
            return doc.RootElement
                .GetProperty("parcel")
                .GetProperty("lotNumber")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}

public record ScraperConfig
{
    /// <summary>true = láthatatlan böngésző (ajánlott éles használathoz)</summary>
    public bool Headless { get; init; } = true;

    /// <summary>Milliszekundumos lassítás lépések közt (0 = nincs)</summary>
    public int SlowMo { get; init; } = 50;

    /// <summary>Keresések közt várakozás (ms) — ne terheljük a szervert</summary>
    public int DelayBetweenSearchesMs { get; init; } = 1500;

    /// <summary>Oldalbetöltési timeout (ms)</summary>
    public int NavigationTimeoutMs { get; init; } = 30_000;

    /// <summary>DOM elem megjelenési timeout (ms)</summary>
    public int ElementTimeoutMs { get; init; } = 10_000;
}