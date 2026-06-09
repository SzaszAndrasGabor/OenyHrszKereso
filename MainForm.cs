using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace OenyHrszKereso;

public class MainForm : Form
{
    // ── Vezérlők ─────────────────────────────────────────────────────────────
    private Label _lblBemenet = null!;
    private TextBox _txtBemenet = null!;
    private Button _btnFajlValaszt = null!;
    private Button _btnInditas = null!;
    private Button _btnSzunet = null!;
    private Button _btnFolytatas = null!;
    private Button _btnLeallitas = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblStatusz = null!;
    private Label _lblEredmeny = null!;
    private CheckBox _chkLathato = null!;
    private Button _btnOszlopok = null!;

    // ── Futtatás ─────────────────────────────────────────────────────────────
    private CancellationTokenSource? _cts;
    private string? _inputFile;
    private string? _outputFile;
    private string? _outputFilePath;

    // Oszlop hozzárendelések (1-alapú, 0 = nincs)
    private int _varosOszlop = 1;
    private int _hrszOszlop  = 2;
    private int _cimOszlop   = 3;

    // Szünet vezérlése
    private volatile bool _szunetel = false;
    private readonly SemaphoreSlim _szunetSemaphore = new SemaphoreSlim(1, 1);

    public MainForm()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Text            = "OÉNY HRSZ Kereső";
        Size            = new Size(640, 340);
        MinimumSize     = new Size(540, 340);
        MaximumSize     = new Size(900, 340);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        Font            = new Font("Segoe UI", 9.5f);

        // ── Fájlválasztó sor ─────────────────────────────────────────────────
        _lblBemenet = new Label
        {
            Text      = "Bemeneti fájl:",
            Location  = new Point(12, 20),
            Size      = new Size(100, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _txtBemenet = new TextBox
        {
            Location  = new Point(115, 18),
            Size      = new Size(360, 24),
            ReadOnly  = true,
            BackColor = Color.White,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _btnFajlValaszt = new Button
        {
            Text   = "Tallózás...",
            Location = new Point(485, 17),
            Size   = new Size(90, 26),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _btnFajlValaszt.Click += BtnFajlValaszt_Click;

        _btnOszlopok = new Button
        {
            Text     = "Oszlopok...",
            Location = new Point(485, 50),
            Size     = new Size(90, 26),
            Enabled  = false,
            Anchor   = AnchorStyles.Right | AnchorStyles.Top
        };
        _btnOszlopok.Click += BtnOszlopok_Click;

        // ── Látható böngésző ─────────────────────────────────────────────────
        _chkLathato = new CheckBox
        {
            Text     = "Látható böngésző (lassabb, hibakereséshez)",
            Location = new Point(115, 52),
            Size     = new Size(350, 22),
            Checked  = false
        };

        // ── Folyamatjelző ────────────────────────────────────────────────────
        _progressBar = new ProgressBar
        {
            Location = new Point(12, 88),
            Size     = new Size(603, 22),
            Minimum  = 0,
            Maximum  = 100,
            Value    = 0,
            Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        // ── Státusz felirat ──────────────────────────────────────────────────
        _lblStatusz = new Label
        {
            Text      = "Válassz ki egy bemeneti Excel fájlt.",
            Location  = new Point(12, 118),
            Size      = new Size(603, 22),
            ForeColor = Color.DimGray,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        // ── Eredmény felirat ─────────────────────────────────────────────────
        _lblEredmeny = new Label
        {
            Text     = "",
            Location = new Point(12, 145),
            Size     = new Size(603, 44),
            ForeColor = Color.DarkGreen,
            Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            AutoSize = false
        };
        _lblEredmeny.Click += LblEredmeny_Click;

        // ── Gombok ───────────────────────────────────────────────────────────
        _btnInditas = MakeButton("▶  Indítás",   new Point(12,  220), Color.FromArgb(0, 122, 204));
        _btnInditas.Enabled = false;
        _btnInditas.Click  += BtnInditas_Click;

        _btnSzunet = MakeButton("⏸  Szünet",    new Point(155, 220), Color.FromArgb(180, 130, 0));
        _btnSzunet.Enabled = false;
        _btnSzunet.Click  += BtnSzunet_Click;

        _btnFolytatas = MakeButton("▶▶  Folytatás", new Point(298, 220), Color.FromArgb(0, 150, 80));
        _btnFolytatas.Enabled = false;
        _btnFolytatas.Click  += BtnFolytatas_Click;

        _btnLeallitas = MakeButton("■  Leállítás", new Point(441, 220), Color.FromArgb(200, 50, 50));
        _btnLeallitas.Enabled = false;
        _btnLeallitas.Click  += BtnLeallitas_Click;

        // ── Vezérlők hozzáadása ──────────────────────────────────────────────
        Controls.AddRange(new Control[]
        {
            _lblBemenet, _txtBemenet, _btnFajlValaszt, _btnOszlopok,
            _chkLathato,
            _progressBar,
            _lblStatusz, _lblEredmeny,
            _btnInditas, _btnSzunet, _btnFolytatas, _btnLeallitas
        });

        FormClosing += MainForm_FormClosing;
    }

    private static Button MakeButton(string text, Point location, Color backColor)
    {
        var btn = new Button
        {
            Text      = text,
            Location  = location,
            Size      = new Size(130, 36),
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Anchor    = AnchorStyles.Left | AnchorStyles.Bottom
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    // ── Fájlválasztó ─────────────────────────────────────────────────────────
    private void BtnFajlValaszt_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Válaszd ki a bemeneti Excel fájlt",
            Filter = "Excel fájlok (*.xlsx)|*.xlsx|Minden fájl (*.*)|*.*"
        };

        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _inputFile       = dlg.FileName;
            var dir          = Path.GetDirectoryName(_inputFile) ?? ".";
            var name         = Path.GetFileNameWithoutExtension(_inputFile);
            _outputFile      = Path.Combine(dir, $"{name}_eredmenyek.xlsx");
            _txtBemenet.Text = _inputFile;
            _btnInditas.Enabled  = true;
            _btnOszlopok.Enabled = true;
            SetStatusz($"Fájl betöltve: {Path.GetFileName(_inputFile)}", Color.DimGray);
            _lblEredmeny.Text  = "";
            _progressBar.Value = 0;
        }
    }

    // ── Oszlopok ─────────────────────────────────────────────────────────────
    private void BtnOszlopok_Click(object? sender, EventArgs e)
    {
        if (_inputFile == null) return;

        using var dlg = new OszlopValasztoDialog(_inputFile)
        {
            // Átadjuk az aktuális beállításokat
        };

        // Visszaállítjuk az előző választást
        dlg.Tag = (_varosOszlop, _hrszOszlop, _cimOszlop);

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _varosOszlop = dlg.VarosOszlop;
            _hrszOszlop  = dlg.HrszOszlop;
            _cimOszlop   = dlg.CimOszlop;

            SetStatusz($"Oszlopok: Település={OszlopBetu(_varosOszlop)}  " +
                       $"HRSZ={OszlopBetu(_hrszOszlop)}  " +
                       $"Cím={OszlopBetu(_cimOszlop)}", Color.DimGray);
        }
    }

    private static string OszlopBetu(int idx) =>
        idx == 0 ? "–" : ((char)('A' + idx - 1)).ToString();

    // ── Indítás ───────────────────────────────────────────────────────────────
    private async void BtnInditas_Click(object? sender, EventArgs e)
    {
        if (_inputFile == null) return;

        _cts      = new CancellationTokenSource();
        _szunetel = false;
        if (_szunetSemaphore.CurrentCount == 0)
            _szunetSemaphore.Release();

        SetFutasAllapot(true);
        _lblEredmeny.Text  = "";
        _progressBar.Value = 0;

        try
        {
            await FuttatasAsync(_inputFile, _outputFile!, _cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatusz($"Hiba: {ex.Message}", Color.Red);
        }
        finally
        {
            if (_szunetSemaphore.CurrentCount == 0)
                _szunetSemaphore.Release();
            SetFutasAllapot(false);
        }
    }

    // ── Szünet ────────────────────────────────────────────────────────────────
    private void BtnSzunet_Click(object? sender, EventArgs e)
    {
        _szunetel = true;
        _szunetSemaphore.Wait(0); // elveszi a tokent → a futás megáll
        SetStatusz("Szünetelve — nyomj Folytatást a folytatáshoz.", Color.DarkOrange);
        _btnSzunet.Enabled    = false;
        _btnFolytatas.Enabled = true;
    }

    // ── Folytatás ─────────────────────────────────────────────────────────────
    private void BtnFolytatas_Click(object? sender, EventArgs e)
    {
        _szunetel = false;
        if (_szunetSemaphore.CurrentCount == 0)
            _szunetSemaphore.Release(); // felengedi → a futás folytatódik
        SetStatusz("Folytatás...", Color.DimGray);
        _btnSzunet.Enabled    = true;
        _btnFolytatas.Enabled = false;
    }

    // ── Leállítás ─────────────────────────────────────────────────────────────
    private void BtnLeallitas_Click(object? sender, EventArgs e)
    {
        // Ha szünetelve van, először engedjük el
        if (_szunetSemaphore.CurrentCount == 0)
            _szunetSemaphore.Release();
        _szunetel = false;
        _cts?.Cancel();
        SetStatusz("Leállítás folyamatban...", Color.DarkOrange);
        _btnLeallitas.Enabled = false;
        _btnSzunet.Enabled    = false;
        _btnFolytatas.Enabled = false;
    }

    // ── Fő futtatási logika ───────────────────────────────────────────────────
    private async Task FuttatasAsync(string inputFile, string outputFile, CancellationToken ct)
    {
        var config = new ScraperConfig
        {
            Headless               = !_chkLathato.Checked,
            SlowMo                 = _chkLathato.Checked ? 200 : 0,
            DelayBetweenSearchesMs = 500,
            NavigationTimeoutMs    = 30_000,
            ElementTimeoutMs       = 10_000,
        };

        var services = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddSingleton(config)
            .AddSingleton<ExcelService>()
            .AddSingleton<OenyScraperService>()
            .BuildServiceProvider();

        var logger  = services.GetRequiredService<ILogger<MainForm>>();
        var excel   = services.GetRequiredService<ExcelService>();
        var scraper = services.GetRequiredService<OenyScraperService>();

        try
        {
            SetStatusz("Bemeneti fájl beolvasása...", Color.DimGray);
            var rows = excel.ReadInput(inputFile, _varosOszlop, _hrszOszlop, _cimOszlop);

            if (rows.Count == 0)
            {
                SetStatusz("Nincs feldolgozandó sor a fájlban.", Color.DarkOrange);
                return;
            }

            SetProgressMax(rows.Count);

            SetStatusz("Böngésző inicializálása...", Color.DimGray);
            await scraper.InitAsync();

            var allResults = new List<SearchResult>();
            int idx = 0;

            try
            {
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();

                    // ── Szünet kezelése ───────────────────────────────────────────
                    if (_szunetel)
                    {
                        await _szunetSemaphore.WaitAsync(ct);
                        _szunetSemaphore.Release();
                        ct.ThrowIfCancellationRequested();
                    }

                    idx++;
                    var ertek = !string.IsNullOrWhiteSpace(row.Hrsz) ? row.Hrsz : row.Cim;
                    SetStatusz($"[{idx}/{rows.Count}]  {row.Varos}  /  {ertek}", Color.DimGray);
                    SetProgress(idx - 1);

                    var results = await scraper.SearchAsync(row);
                    allResults.AddRange(results);

                    logger.LogInformation("[{Idx}/{Total}] {Varos}/{Ertek} → {Db} találat",
                        idx, rows.Count, row.Varos, ertek, results.Count(r => r.Sikeres));

                    if (idx < rows.Count)
                        await Task.Delay(config.DelayBetweenSearchesMs, ct);
                }

                // Minden sor feldolgozva
                SetProgress(rows.Count);
                int osszesSikeres = allResults.Count(r => r.Sikeres);
                SetStatusz($"Kész! {osszesSikeres}/{allResults.Count} sikeres találat.", Color.DarkGreen);
            }
            catch (OperationCanceledException)
            {
                SetStatusz($"Leállítva ({idx}/{rows.Count} sor feldolgozva).", Color.DarkOrange);
            }
            finally
            {
                // Excel mentés mindig megtörténik — leállításkor is
                if (allResults.Count > 0)
                {
                    SetStatusz(allResults.Count > 0
                        ? $"Eredmények mentése ({allResults.Count} sor)..."
                        : "Nincs mentendő adat.", Color.DimGray);
                    excel.WriteResults(outputFile, allResults);
                    SetEredmeny($"Kimenet: {outputFile}", outputFile);
                }
            }
        }
        finally
        {
            await scraper.DisposeAsync();
        }
    }

    // ── Ablak bezárása ────────────────────────────────────────────────────────
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_szunetSemaphore.CurrentCount == 0)
            _szunetSemaphore.Release();
        _cts?.Cancel();
    }

    // ── UI segédmetódusok (thread-safe) ───────────────────────────────────────
    private void SetStatusz(string szoveg, Color szin)
    {
        if (InvokeRequired) Invoke(() => SetStatusz(szoveg, szin));
        else { _lblStatusz.Text = szoveg; _lblStatusz.ForeColor = szin; }
    }

    private void SetEredmeny(string szoveg, string outputFile)
    {
        if (InvokeRequired)
            Invoke(() => SetEredmeny(szoveg, outputFile));
        else
        {
            _outputFilePath        = outputFile;
            _lblEredmeny.Text      = szoveg;
            _lblEredmeny.ForeColor = Color.DarkGreen;
            _lblEredmeny.Cursor    = Cursors.Hand;
        }
    }

    private void LblEredmeny_Click(object? sender, EventArgs e)
    {
        if (_outputFilePath == null) return;
        try { Process.Start(new ProcessStartInfo(_outputFilePath) { UseShellExecute = true }); }
        catch { }
    }

    private void SetProgressMax(int max)
    {
        if (InvokeRequired) Invoke(() => SetProgressMax(max));
        else _progressBar.Maximum = max;
    }

    private void SetProgress(int ertek)
    {
        if (InvokeRequired) Invoke(() => SetProgress(ertek));
        else _progressBar.Value = Math.Min(ertek, _progressBar.Maximum);
    }

    private void SetFutasAllapot(bool fut)
    {
        if (InvokeRequired)
            Invoke(() => SetFutasAllapot(fut));
        else
        {
            _btnInditas.Enabled     = !fut;
            _btnSzunet.Enabled      =  fut;
            _btnFolytatas.Enabled   = false;
            _btnLeallitas.Enabled   =  fut;
            _btnFajlValaszt.Enabled = !fut;
            _btnOszlopok.Enabled    = !fut && _inputFile != null;
            _chkLathato.Enabled     = !fut;
        }
    }
}
