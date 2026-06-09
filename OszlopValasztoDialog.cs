using System.Windows.Forms;
using ClosedXML.Excel;

namespace OenyHrszKereso;

/// <summary>
/// Párbeszédablak az Excel oszlopok hozzárendeléséhez.
/// Megmutatja az Excel fejléc sorát és legördülő listából lehet kiválasztani
/// melyik oszlop melyik adatot tartalmazza.
/// </summary>
public class OszlopValasztoDialog : Form
{
    // Kiválasztott oszlopok (1-alapú index)
    public int VarosOszlop { get; private set; } = 1;  // A
    public int HrszOszlop  { get; private set; } = 2;  // B
    public int CimOszlop   { get; private set; } = 3;  // C

    private ComboBox _cmbVaros = null!;
    private ComboBox _cmbHrsz  = null!;
    private ComboBox _cmbCim   = null!;
    private Label    _lblElonezet = null!;
    private Button   _btnOk    = null!;
    private Button   _btnMegse = null!;

    private readonly List<string> _oszlopNevek;
    private readonly List<string[]> _elonezetSorok;

    public OszlopValasztoDialog(string excelFajl)
    {
        _oszlopNevek   = new List<string>();
        _elonezetSorok = new List<string[]>();

        // Excel beolvasása előnézethez
        try
        {
            using var wb    = new XLWorkbook(excelFajl);
            var sheet       = wb.Worksheets.First();
            int utolsoOszlop = sheet.LastColumnUsed()?.ColumnNumber() ?? 26;
            int utolsoSor    = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 4);

            // Oszlopnevek: A, B, C... + fejléc tartalom ha van
            for (int c = 1; c <= utolsoOszlop; c++)
            {
                var fejlecSzoveg = sheet.Cell(1, c).GetString().Trim();
                var oszlopBetu   = ColumnIndexToLetter(c);
                var nev = string.IsNullOrEmpty(fejlecSzoveg)
                    ? oszlopBetu
                    : $"{oszlopBetu} – {fejlecSzoveg}";
                _oszlopNevek.Add(nev);
            }

            // Első néhány sor előnézethez
            for (int r = 1; r <= utolsoSor; r++)
            {
                var sor = new string[utolsoOszlop];
                for (int c = 1; c <= utolsoOszlop; c++)
                    sor[c - 1] = sheet.Cell(r, c).GetString().Trim();
                _elonezetSorok.Add(sor);
            }
        }
        catch
        {
            // Ha nem sikerül beolvasni, alapértelmezett A/B/C oszlopok
            for (int c = 1; c <= 10; c++)
                _oszlopNevek.Add(ColumnIndexToLetter(c));
        }

        InitializeComponents();
        FrissitElonezet();
    }

    private void InitializeComponents()
    {
        Text            = "Oszlopok hozzárendelése";
        Size            = new Size(480, 320);
        MinimumSize     = new Size(480, 320);
        MaximumSize     = new Size(700, 320);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        Font            = new Font("Segoe UI", 9.5f);

        // ── Fejléc szöveg ─────────────────────────────────────────────────────
        var lblInfo = new Label
        {
            Text     = "Rendeld hozzá az Excel oszlopait a megfelelő adatmezőkhöz:",
            Location = new Point(12, 12),
            Size     = new Size(440, 20),
            Font     = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        // ── Oszlop választók ─────────────────────────────────────────────────
        var lblVaros = MakeLabel("Település oszlop:", new Point(12, 45));
        _cmbVaros    = MakeCombo(new Point(180, 42));

        var lblHrsz  = MakeLabel("HRSZ oszlop:", new Point(12, 80));
        _cmbHrsz     = MakeCombo(new Point(180, 77));

        var lblCim   = MakeLabel("Cím oszlop:", new Point(12, 115));
        _cmbCim      = MakeCombo(new Point(180, 112));

        // Feltöltés és alapértelmezés
        var semmiItem = "– nincs –";
        foreach (var combo in new[] { _cmbVaros, _cmbHrsz, _cmbCim })
        {
            combo.Items.Add(semmiItem);
            foreach (var nev in _oszlopNevek)
                combo.Items.Add(nev);
        }

        // ── Előnézet — ELŐBB inicializáljuk, csak aztán állítjuk be a combo értékeket
        var lblElonezetCim = MakeLabel("Előnézet:", new Point(12, 152));
        _lblElonezet = new Label
        {
            Location  = new Point(12, 172),
            Size      = new Size(440, 60),
            Font      = new Font("Courier New", 8.5f),
            ForeColor = Color.DarkSlateGray,
            AutoSize  = false
        };

        // Csak az inicializálás UTÁN állítjuk be a kiválasztott értékeket
        // (hogy a SelectedIndexChanged esemény már találja a _lblElonezet-et)
        _cmbVaros.SelectedIndex = Math.Min(1, _cmbVaros.Items.Count - 1); // A oszlop
        _cmbHrsz.SelectedIndex  = Math.Min(2, _cmbHrsz.Items.Count - 1);  // B oszlop
        _cmbCim.SelectedIndex   = Math.Min(3, _cmbCim.Items.Count - 1);   // C oszlop

        // Esemény hozzárendelése CSAK az inicializálás után
        _cmbVaros.SelectedIndexChanged += (_, _) => FrissitElonezet();
        _cmbHrsz.SelectedIndexChanged  += (_, _) => FrissitElonezet();
        _cmbCim.SelectedIndexChanged   += (_, _) => FrissitElonezet();

        // Első megnyitáskor is megjelenjen az előnézet
        FrissitElonezet();

        // ── OK / Mégse gombok ────────────────────────────────────────────────
        _btnOk = new Button
        {
            Text         = "OK",
            DialogResult = DialogResult.OK,
            Location     = new Point(280, 248),
            Size         = new Size(80, 28),
            BackColor    = Color.FromArgb(0, 122, 204),
            ForeColor    = Color.White,
            FlatStyle    = FlatStyle.Flat
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += BtnOk_Click;

        _btnMegse = new Button
        {
            Text         = "Mégse",
            DialogResult = DialogResult.Cancel,
            Location     = new Point(372, 248),
            Size         = new Size(80, 28),
            FlatStyle    = FlatStyle.Flat
        };

        AcceptButton = _btnOk;
        CancelButton = _btnMegse;

        Controls.AddRange(new Control[]
        {
            lblInfo,
            lblVaros, _cmbVaros,
            lblHrsz,  _cmbHrsz,
            lblCim,   _cmbCim,
            lblElonezetCim, _lblElonezet,
            _btnOk, _btnMegse
        });
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        // 0. elem = "– nincs –", 1. elem = A oszlop (index 1), stb.
        VarosOszlop = _cmbVaros.SelectedIndex; // 0 = nincs
        HrszOszlop  = _cmbHrsz.SelectedIndex;
        CimOszlop   = _cmbCim.SelectedIndex;

        if (VarosOszlop == 0)
        {
            MessageBox.Show("A Település oszlop megadása kötelező!",
                "Hiányzó adat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (HrszOszlop == 0 && CimOszlop == 0)
        {
            MessageBox.Show("Legalább a HRSZ vagy a Cím oszlopot meg kell adni!",
                "Hiányzó adat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
    }

    private void FrissitElonezet()
    {
        if (_lblElonezet == null) return;
        if (_elonezetSorok.Count == 0) return;

        var varosIdx = _cmbVaros.SelectedIndex - 1; // -1 = nincs
        var hrszIdx  = _cmbHrsz.SelectedIndex  - 1;
        var cimIdx   = _cmbCim.SelectedIndex   - 1;

        var sorok = new List<string>();
        foreach (var sor in _elonezetSorok.Take(3))
        {
            var varos = varosIdx >= 0 && varosIdx < sor.Length ? sor[varosIdx] : "-";
            var hrsz  = hrszIdx  >= 0 && hrszIdx  < sor.Length ? sor[hrszIdx]  : "-";
            var cim   = cimIdx   >= 0 && cimIdx   < sor.Length ? sor[cimIdx]   : "-";

            var sor_text = $"T: {Truncate(varos, 14)}  H: {Truncate(hrsz, 10)}  C: {Truncate(cim, 16)}";
            sorok.Add(sor_text);
        }

        _lblElonezet.Text = string.Join(Environment.NewLine, sorok);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.PadRight(max) : s[..(max - 1)] + "…";

    private static Label MakeLabel(string text, Point location) => new Label
    {
        Text      = text,
        Location  = location,
        Size      = new Size(165, 22),
        TextAlign = ContentAlignment.MiddleRight
    };

    private static ComboBox MakeCombo(Point location) => new ComboBox
    {
        Location      = location,
        Size          = new Size(270, 24),
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor        = AnchorStyles.Left | AnchorStyles.Top
    };

    private static string ColumnIndexToLetter(int index)
    {
        var result = "";
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index  /= 26;
        }
        return result;
    }
}
