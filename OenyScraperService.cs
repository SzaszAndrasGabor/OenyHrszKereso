using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace OenyHrszKereso;

public class OenyScraperService : IAsyncDisposable
{
    private readonly ILogger<OenyScraperService> _logger;
    private readonly ScraperConfig _config;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    private double? _eovX;
    private double? _eovY;

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
            ExecutablePath = FindChrome()
        });

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "hu-HU",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            IgnoreHTTPSErrors = true
        });

        _page = await context.NewPageAsync();
        _page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        // Globális Response figyelő
        // HRSZ mód: /hk-api/parcels/bounding-box  → boundingBox.min/max átlag
        // Cím mód:  /hk-api/addresses/position     → point.x / point.y
        _page.Response += async (_, response) =>
        {
            try
            {
                var url = response.Url;
                if (!url.Contains("bounding-box") && !url.Contains("addresses/position")) return;

                var body = await response.TextAsync();
                var doc = System.Text.Json.JsonDocument.Parse(body);

                if (url.Contains("addresses/position"))
                {
                    var point = doc.RootElement.GetProperty("point");
                    _eovX = Math.Round(point.GetProperty("x").GetDouble(), 1);
                    _eovY = Math.Round(point.GetProperty("y").GetDouble(), 1);
                    _logger.LogInformation("EOV [Cím]: X={X}, Y={Y}", _eovX, _eovY);
                }
                else
                {
                    var bb = doc.RootElement.GetProperty("boundingBox");
                    var min = bb.GetProperty("min");
                    var max = bb.GetProperty("max");
                    _eovX = Math.Round((min.GetProperty("x").GetDouble() + max.GetProperty("x").GetDouble()) / 2, 1);
                    _eovY = Math.Round((min.GetProperty("y").GetDouble() + max.GetProperty("y").GetDouble()) / 2, 1);
                    _logger.LogInformation("EOV [HRSZ]: X={X}, Y={Y}", _eovX, _eovY);
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
        catch { }
    }

    /// <summary>
    /// Autocomplete mezőbe ír és megkeresi a pontosan egyező elemet a dropdownban.
    /// Chrome-ban az Angular overlay a body-ba rendereli a dropdownt,
    /// ezért több szelektort próbálunk.
    /// </summary>
    private async Task<bool> AutocompleteKivalasztAsync(ILocator input, string ertek, string mezoNev)
    {
        await input.ClickAsync();
        await input.FillAsync(ertek);
        await _page!.WaitForTimeoutAsync(1500);

        // Chrome-ban az Angular overlay a body-ba rendereli a dropdownt
        var dropdownSzelektorok = new[]
        {
            "li.p-autocomplete-item",
            ".p-autocomplete-panel li",
            ".p-overlay li",
            "ul.p-autocomplete-items li",
            "[class*='autocomplete'] li",
        };

        IReadOnlyList<IElementHandle>? options = null;
        foreach (var szelektor in dropdownSzelektorok)
        {
            var found = await _page.QuerySelectorAllAsync(szelektor);
            if (found.Count > 0)
            {
                _logger.LogInformation("{Mezo}: dropdown ({Szelektor}), {Db} elem",
                    mezoNev, szelektor, found.Count);
                options = found;
                break;
            }
        }

        if (options == null || options.Count == 0)
        {
            // Utolsó esély: várunk még 2mp és újra próbálunk
            await _page.WaitForTimeoutAsync(2000);
            options = await _page.QuerySelectorAllAsync("li.p-autocomplete-item");
            if (options.Count == 0)
            {
                _logger.LogWarning("{Mezo}: dropdown nem jelent meg '{Ertek}' beírása után.", mezoNev, ertek);
                return false;
            }
        }

        // Pontos egyezés keresése
        foreach (var option in options)
        {
            var text = (await option.InnerTextAsync()).Trim();
            _logger.LogInformation("DROPDOWN ELEM: '{Text}'", text);
            if (string.Equals(text, ertek, StringComparison.OrdinalIgnoreCase))
            {
                await option.EvaluateAsync("el => el.click()");
                _logger.LogInformation("{Mezo}: kiválasztva: '{Ertek}'", mezoNev, ertek);
                await _page.WaitForTimeoutAsync(300);
                return true;
            }
        }

        // Nincs pontos egyezés
        var lehetosegek = new List<string>();
        foreach (var o in options)
            lehetosegek.Add((await o.InnerTextAsync()).Trim());
        _logger.LogWarning("{Mezo}: nincs pontos egyezés '{Ertek}'. Opciók: {Opciok}",
            mezoNev, ertek, string.Join(", ", lehetosegek));

        await input.PressAsync("Escape");
        await _page.WaitForTimeoutAsync(300);
        return false;
    }

    public async Task<List<SearchResult>> SearchAsync(InputRow input)
    {
        bool hrszMod = !string.IsNullOrWhiteSpace(input.Hrsz);

        _logger.LogInformation("Keresés [{Mod}]: {Varos} / {Ertek}",
            hrszMod ? "HRSZ" : "Cím",
            input.Varos,
            hrszMod ? input.Hrsz : input.Cim);

        _eovX = null;
        _eovY = null;

        try
        {
            await _page!.GotoAsync(BaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = _config.NavigationTimeoutMs
            });

            // ── 1. LÉPÉS: Település mező — pontos egyezés ────────────────────────
            var settlementInput = _page.Locator("input.p-autocomplete-input").Nth(0);
            await settlementInput.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _config.ElementTimeoutMs
            });

            bool varosOk = await AutocompleteKivalasztAsync(settlementInput, input.Varos, "Település");
            if (!varosOk)
            {
                return new List<SearchResult> { new SearchResult
                {
                    Varos       = input.Varos,
                    InputHrsz   = input.Hrsz,
                    InputCim    = input.Cim,
                    KeresesiMod = hrszMod ? "HRSZ" : "Cím",
                    Sikeres     = false,
                    Hiba        = $"Település nem található pontosan: {input.Varos}"
                }};
            }

            _logger.LogInformation("Település kiválasztva: {Varos}", input.Varos);

            if (hrszMod)
                return await SearchByHrszAsync(input);
            else
                return await SearchByCimAsync(input);
        }
        catch (TimeoutException tex)
        {
            _logger.LogError("Időtúllépés: {Msg}", tex.Message);
            return new List<SearchResult> { ErrorResult(input, $"Időtúllépés: {tex.Message}") };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Váratlan hiba");
            return new List<SearchResult> { ErrorResult(input, ex.Message) };
        }
    }

    // ── HRSZ alapú keresés ────────────────────────────────────────────────────
    private async Task<List<SearchResult>> SearchByHrszAsync(InputRow input)
    {
        await _page!.WaitForTimeoutAsync(400);
        var hrszTab = _page.Locator(
            "label:has-text('Helyrajzi'), button:has-text('Helyrajzi'), " +
            ".p-button:has-text('Helyrajzi'), [role='tab']:has-text('Helyrajzi'), " +
            "span:has-text('Helyrajzi szám')"
        ).First;
        await hrszTab.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _config.ElementTimeoutMs
        });
        await hrszTab.ClickAsync();
        _logger.LogInformation("'Helyrajzi szám' mód kiválasztva.");
        await _page.WaitForTimeoutAsync(200);

        var hrszInput = _page.Locator("input.p-autocomplete-input").Nth(1);
        await hrszInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _config.ElementTimeoutMs
        });

        bool hrszOk = await AutocompleteKivalasztAsync(hrszInput, input.Hrsz, "HRSZ");
        if (!hrszOk)
        {
            return new List<SearchResult> { new SearchResult
            {
                Varos       = input.Varos,
                InputHrsz   = input.Hrsz,
                KeresesiMod = "HRSZ",
                Sikeres     = false,
                Hiba        = $"HRSZ nem található pontosan: {input.Hrsz}"
            }};
        }

        _logger.LogInformation("HRSZ kiválasztva: {Hrsz}", input.Hrsz);
        await WaitForApiResponseAsync("bounding-box");
        return await ExtractCardsAsync(input, "HRSZ");
    }

    // ── Cím alapú keresés ─────────────────────────────────────────────────────
    private async Task<List<SearchResult>> SearchByCimAsync(InputRow input)
    {
        await _page!.WaitForTimeoutAsync(800);

        try
        {
            var cimTab = _page.Locator(
                "label:has-text('Cím'), button:has-text('Cím'), " +
                "[role='tab']:has-text('Cím'), span:has-text('Cím')"
            ).First;
            if (await cimTab.IsVisibleAsync())
            {
                await cimTab.ClickAsync();
                await _page.WaitForTimeoutAsync(100);
            }
        }
        catch { }

        _logger.LogInformation("'Cím' mód kiválasztva.");

        ILocator cimInput;
        try
        {
            var visible = _page.Locator("input.p-autocomplete-input:visible").Nth(1);
            await visible.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 3000
            });
            cimInput = visible;
        }
        catch
        {
            cimInput = _page.Locator("input.p-autocomplete-input:visible").Last;
            await cimInput.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = _config.ElementTimeoutMs
            });
        }

        bool cimOk = await AutocompleteKivalasztAsync(cimInput, input.Cim, "Cím");
        if (!cimOk)
        {
            return new List<SearchResult> { new SearchResult
            {
                Varos       = input.Varos,
                InputCim    = input.Cim,
                KeresesiMod = "Cím",
                Sikeres     = false,
                Hiba        = $"Cím nem található pontosan: {input.Cim}"
            }};
        }

        _logger.LogInformation("Cím kiválasztva: {Cim}", input.Cim);
        await WaitForApiResponseAsync("addresses/position");
        return await ExtractCardsAsync(input, "Cím");
    }

    private async Task WaitForApiResponseAsync(string urlPart)
    {
        try
        {
            await _page!.WaitForResponseAsync(
                r => r.Url.Contains(urlPart),
                new PageWaitForResponseOptions { Timeout = 3000 });
            await _page.WaitForTimeoutAsync(300);
            _logger.LogInformation("API válasz megérkezett: {UrlPart}", urlPart);
        }
        catch
        {
            _logger.LogWarning("API válasz nem érkezett: {UrlPart}", urlPart);
            await _page!.WaitForTimeoutAsync(100);
        }
    }

    // ── Kártyák kinyerése ─────────────────────────────────────────────────────
    private async Task<List<SearchResult>> ExtractCardsAsync(InputRow input, string mod)
    {
        var results = new List<SearchResult>();
        var currentUrl = _page!.Url;
        bool urlOk = currentUrl != BaseUrl && currentUrl.Contains("state=");

        // Megvárjuk hogy Angular renderje a tulajdoni lap linkeket
        await _page.WaitForTimeoutAsync(500);

        var cards = await _page.QuerySelectorAllAsync("div.result-card");

        if (cards.Count == 0)
        {
            _logger.LogWarning("Nem találhatók kártyák: {Varos} / {Ertek}",
                input.Varos, mod == "HRSZ" ? input.Hrsz : input.Cim);
            results.Add(new SearchResult
            {
                Varos = input.Varos,
                InputHrsz = input.Hrsz,
                InputCim = input.Cim,
                KeresesiMod = mod,
                TerkeLink = urlOk ? currentUrl : null,
                EovX = _eovX?.ToString("F1"),
                EovY = _eovY?.ToString("F1"),
                Sikeres = false,
                Hiba = "Nem található találat"
            });
            return results;
        }

        _logger.LogInformation("  {Count} result-card megtalálva", cards.Count);

        foreach (var card in cards)
        {
            try
            {
                // ── Cím ──────────────────────────────────────────────────────────
                string? cim = null;
                var cimEl = await card.QuerySelectorAsync(".result-card-address, .result-card-title, h3, h4");
                if (cimEl != null)
                    cim = (await cimEl.InnerTextAsync()).Trim();

                if (string.IsNullOrEmpty(cim))
                {
                    var fullText = (await card.InnerTextAsync()).Trim();
                    cim = fullText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .FirstOrDefault(l => l.Length > 3 && !l.StartsWith("HRSZ"));
                }

                // ── HRSZ ─────────────────────────────────────────────────────────
                string? hrsz = null;
                var hrszEl = await card.QuerySelectorAsync(".result-card-hrsz");
                if (hrszEl != null)
                    hrsz = (await hrszEl.InnerTextAsync()).Replace("HRSZ:", "").Trim();

                if (string.IsNullOrEmpty(hrsz))
                {
                    var cardText = (await card.InnerTextAsync()).Trim();
                    var hrszLine = cardText.Split('\n')
                        .Select(l => l.Trim())
                        .FirstOrDefault(l => l.StartsWith("HRSZ:"));
                    if (hrszLine != null)
                        hrsz = hrszLine.Replace("HRSZ:", "").Trim();
                }
                hrsz ??= input.Hrsz;

                // ── Albetétek ─────────────────────────────────────────────────────
                int? albetek = null;
                var parcelEl = await card.QuerySelectorAsync("div.result-card-parcel");
                if (parcelEl != null)
                {
                    var parcelText = (await parcelEl.InnerTextAsync()).Trim();
                    var match = Regex.Match(parcelText, @"(\d+)\s*db", RegexOptions.IgnoreCase);
                    if (match.Success)
                        albetek = int.Parse(match.Groups[1].Value);
                }

                // ── Tulajdoni lap link ────────────────────────────────────────────
                string? tulajdoniLapLink = null;
                try
                {
                    await _page.WaitForSelectorAsync(
                        "a.property-sheet-navigation-link",
                        new PageWaitForSelectorOptions { Timeout = 3000 });

                    tulajdoniLapLink = await card.EvaluateAsync<string?>(
                        "el => { const a = el.querySelector('a.property-sheet-navigation-link'); return a ? a.href : null; }");
                }
                catch { }

                results.Add(new SearchResult
                {
                    Varos = input.Varos,
                    InputHrsz = input.Hrsz,
                    InputCim = input.Cim,
                    KeresesiMod = mod,
                    Cim = cim,
                    TalalaltHrsz = hrsz,
                    TerkeLink = currentUrl != BaseUrl ? currentUrl : null,
                    EovX = _eovX?.ToString("F1"),
                    EovY = _eovY?.ToString("F1"),
                    Albetek = albetek,
                    TulajdoniLapLink = tulajdoniLapLink,
                    Sikeres = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Kártya feldolgozási hiba: {Msg}", ex.Message);
            }
        }

        if (results.Count == 0)
            results.Add(ErrorResult(input, "Kártyák feldolgozása sikertelen"));

        _logger.LogInformation("  {Count} kártya feldolgozva [{Mod}]", results.Count, mod);
        return results;
    }

    private static SearchResult ErrorResult(InputRow input, string hiba) => new()
    {
        Varos = input.Varos,
        InputHrsz = input.Hrsz,
        InputCim = input.Cim,
        KeresesiMod = string.IsNullOrWhiteSpace(input.Hrsz) ? "Cím" : "HRSZ",
        Sikeres = false,
        Hiba = hiba
    };

    private static string? FindChrome()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
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
    public bool Headless { get; init; } = true;
    public int SlowMo { get; init; } = 80;
    public int DelayBetweenSearchesMs { get; init; } = 1000;
    public int NavigationTimeoutMs { get; init; } = 30_000;
    public int ElementTimeoutMs { get; init; } = 10_000;
}