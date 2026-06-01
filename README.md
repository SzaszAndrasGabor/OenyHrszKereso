# OÉNY HRSZ Kereső – C# / Playwright automatizálás

Automatikusan keres helyrajzi számokat az [OÉNY HRSZ Kereső](https://www.oeny.hu/oeny/hrsz-kereso/) oldalon,
Excel bemeneti fájl alapján, és az eredményeket egy kimeneti Excel fájlba írja.

---

## Telepítés

### 1. Előfeltételek
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Internet-kapcsolat

### 2. NuGet csomagok visszaállítása
```bash
dotnet restore
```

### 3. Playwright böngésző telepítése *(csak első alkalommal!)*
```bash
dotnet build
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```
> Ha nincs PowerShell: `dotnet tool install --global Microsoft.Playwright.CLI` majd `playwright install chromium`

---

## Bemeneti Excel formátum

Az `bemenet.xlsx` első munkalapján az **első sor fejléc**, utána az adatsorok:

| Varos         | HRSZ    |
|---------------|---------|
| Balatonfüred  | 2312/2  |
| Budapest      | 12345/6 |
| Pécs          | 987     |

**Elfogadott fejlécek:**
- Város oszlop: `Varos`, `Város`, `Település`, `Telepules`
- HRSZ oszlop: `HRSZ`, `Helyrajzi szám`, `Helyrajzi szam`

---

## Futtatás

```bash
# Alapértelmezett fájlnevekkel (bemenet.xlsx → eredmenyek.xlsx)
dotnet run

# Egyedi fájlnevekkel
dotnet run -- sajat_lista.xlsx kimenet.xlsx
```

---

## Kimeneti Excel tartalma

| Oszlop            | Leírás                                      |
|-------------------|---------------------------------------------|
| Város (bemenet)   | Eredeti bemeneti város                      |
| HRSZ (bemenet)    | Eredeti bemeneti helyrajzi szám             |
| Talált cím        | Az ingatlan utcacíme                        |
| Talált HRSZ       | Az OÉNY által visszaadott HRSZ              |
| Térkép link       | Kattintható link az OÉNY-es találatra       |
| Megjegyzés        | Egyéb megjegyzés                            |
| Státusz           | ✓ Sikeres / ✗ Hiba                          |
| Hiba              | Hibaüzenet (ha volt)                        |

---

## Konfiguráció (`Program.cs`)

```csharp
var config = new ScraperConfig
{
    Headless               = true,   // false = látható böngésző (debugoláshoz)
    SlowMo                 = 50,     // ms lassítás lépések közt
    DelayBetweenSearchesMs = 1500,   // szünet keresések közt (szerver kímélése)
    NavigationTimeoutMs    = 30_000,
    ElementTimeoutMs       = 10_000,
};
```

---

## Fontos megjegyzések

- Az oldal **React/Angular alapú SPA**, a DOM-szelektorok változhatnak.
  Ha hiba lép fel, kapcsold be a `Headless = false` módot és nézd meg, mi történik.
- Az automatizált lekérdezés tömeges használata **nem ajánlott** az OÉNY szervere
  szempontjából — az 1500 ms-os késleltetés szándékos.
- Az OÉNY egy **állami rendszer**, nincs publikus API-ja.
  A szkript saját felelősségre használható.

---

## Hibaelhárítás

| Tünet | Megoldás |
|---|---|
| `ElementTimeoutMs` lejár | `Headless = false` + nézd meg a DOM-ot, frissítsd a szelektorokat |
| Üres találat | Az OÉNY DOM-struktúrája változhatott — ellenőrizd DevTools-szal |
| `playwright.ps1 not found` | Futtasd előbb a `dotnet build`-et |
