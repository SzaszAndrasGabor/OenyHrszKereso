# OÉNY HRSZ Kereső – Fejlesztői és felhasználói dokumentáció

## Tartalomjegyzék

1. [Áttekintés](#1-áttekintés)
2. [Projektstruktúra](#2-projektstruktúra)
3. [Telepítés és futtatás](#3-telepítés-és-futtatás)
4. [Felhasználói útmutató](#4-felhasználói-útmutató)
5. [Fájlok részletes leírása](#5-fájlok-részletes-leírása)
6. [Adatfolyam](#6-adatfolyam)
7. [API végpontok](#7-api-végpontok)
8. [Konfiguráció és hangolás](#8-konfiguráció-és-hangolás)
9. [Hibaelhárítás](#9-hibaelhárítás)
10. [Továbbfejlesztési lehetőségek](#10-továbbfejlesztési-lehetőségek)

---

## 1. Áttekintés

A program automatikusan keres ingatlanokat az [OÉNY HRSZ-kereső](https://www.oeny.hu/oeny/hrsz-kereso/) weboldalon, egy Excel bemeneti fájl alapján. Minden sorhoz visszaadja:

- A talált **cím**et
- A **helyrajzi szám**ot
- Az **EOV koordinátá**kat (Egységes Országos Vetületi rendszer)
- Az **albetétek számá**t (ha van)
- A **térkép link**et (az OÉNY oldalon belüli találat URL-je)
- A **tulajdoni lap link**et (magyarorszag.hu)

### Technológiai stack

| Összetevő | Technológia | Miért ezt? |
|---|---|---|
| UI | WinForms (.NET 8) | Egyszerű, natív Windows ablak |
| Böngésző-automatizálás | Microsoft Playwright | Megbízható, modern SPA-khoz is alkalmas |
| Excel olvasás/írás | ClosedXML | Egyszerű API, .NET-natív |
| Böngésző | Google Chrome (vagy Chromium) | Chrome automatikusan megtalálva |
| Koordináta forrás | OÉNY belső REST API | Nem publikus, de a böngésző kéréseiből kinyerhető |

---

## 2. Projektstruktúra

```
OenyHrszKereso/
├── OenyHrszKereso.csproj      # Projekt konfiguráció, NuGet csomagok
├── Program.cs                 # Belépési pont, STAThread, WinForms indítás
├── MainForm.cs                # Főablak: UI vezérlők, futtatás, szünet/leállítás
├── OszlopValasztoDialog.cs    # Párbeszédablak az Excel oszlopok hozzárendeléséhez
├── OenyScraperService.cs      # Playwright alapú böngésző-automatizálás
├── ExcelService.cs            # Excel beolvasás és kiírás (ClosedXML)
├── Models.cs                  # Adatmodellek: InputRow, SearchResult
└── DOKUMENTACIO.md            # Ez a fájl
```

---

## 3. Telepítés és futtatás

### Fejlesztői környezet

**Előfeltételek:**
- .NET 8 SDK
- Visual Studio 2022 vagy VS Code
- Google Chrome (ajánlott) vagy internet-hozzáférés a Chromium letöltéséhez

**Első indítás:**
```bash
cd C:\Support\hrszProgi
dotnet restore          # NuGet csomagok letöltése
dotnet build            # Fordítás

# Chromium letöltése (csak ha nincs Chrome telepítve):
dotnet run -- install chromium
```

**Futtatás fejlesztői módban:**
```bash
dotnet run
```

### Éles terjesztés (single-file EXE)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Eredmény: `publish\OenyHrszKereso.exe` — ez egyetlen fájl, ami mindent tartalmaz.  
A Chrome-ot a célgépen külön kell telepíteni, vagy első futáskor:
```bash
OenyHrszKereso.exe install chromium
```

---

## 4. Felhasználói útmutató

### Az ablak elemei

```
┌─ OÉNY HRSZ Kereső ──────────────────────────────────────────────┐
│ Bemeneti fájl: [C:\...\bemenet.xlsx              ] [Tallózás]   │
│                                                    [Oszlopok...] │
│ ☑ Látható böngésző (lassabb, hibakereséshez)                    │
│ [████████████░░░░░░░░░░░░░░░░░] folyamatjelző                   │
│ [3/8] Balatonfüred / 2143/9    ← aktuális keresés               │
│ Kimenet: C:\...\bemenet_eredmenyek.xlsx  ← kattintva megnyílik  │
│                                                                  │
│ [▶ Indítás] [⏸ Szünet] [▶▶ Folytatás] [■ Leállítás]            │
└──────────────────────────────────────────────────────────────────┘
```

### Lépések

1. **Tallózás** — válaszd ki a bemeneti Excel fájlt
2. **Oszlopok...** — rendeld hozzá az oszlopokat (alapértelmezett: A=Település, B=HRSZ, C=Cím)
3. **Látható böngésző** — pipáld be ha látni szeretnéd mit csinál a program (lassabb, de jó hibakereséshez)
4. **Indítás** — elindítja a keresést
5. A kimenet automatikusan megnyílik befejezéskor (kattintható link)

### Gombok viselkedése

| Gomb | Mikor aktív | Mit csinál |
|---|---|---|
| Tallózás | Mindig | Fájlválasztó ablak |
| Oszlopok... | Fájl betöltve után | Oszlophozzárendelő párbeszéd |
| Indítás | Fájl betöltve, nem fut | Keresés indítása |
| Szünet | Futás közben | Megáll a következő keresés előtt |
| Folytatás | Szünetelés közben | Folytatja a keresést |
| Leállítás | Futás közben | Leállít, de elmenti az eddigi eredményeket |

### Bemeneti Excel formátum

Az Excel fájl bármilyen oszlopkiosztással működhet — az **Oszlopok...** párbeszédban lehet beállítani melyik oszlop mit tartalmaz.

Alapértelmezett (A/B/C):

| A – Város | B – HRSZ | C – Cím |
|---|---|---|
| Balatonfüred | 2143/9 | |
| Balatonfüred | | Kossuth Lajos utca 5 |
| Gödöllő | 1234/5 | |

**Keresési mód:**
- Ha a HRSZ oszlop **ki van töltve** → HRSZ alapú keresés
- Ha a HRSZ oszlop **üres** → Cím alapú keresés

### Kimeneti Excel oszlopai

| Oszlop | Tartalom |
|---|---|
| Város (bemenet) | Eredeti bemeneti város |
| HRSZ (bemenet) | Eredeti bemeneti HRSZ |
| Cím (bemenet) | Eredeti bemeneti cím |
| Keresési mód | "HRSZ" vagy "Cím" |
| Talált cím | Az ingatlan utcacíme |
| Talált HRSZ | Az OÉNY által visszaadott HRSZ |
| Térkép link | Kattintható link az OÉNY találatra |
| EOV X | Keleti koordináta (Egységes Országos Vetületi) |
| EOV Y | Északi koordináta |
| Albetétek (db) | Albetétek száma (ha van) |
| Státusz | ✓ Sikeres / ✗ Hiba |
| Tulajdoni lap | Kattintható link a magyarorszag.hu-ra |
| Hiba | Hibaüzenet (ha volt) |

---

## 5. Fájlok részletes leírása

### Program.cs

A belépési pont. Két módban indul:
- `OenyHrszKereso.exe install chromium` → Playwright Chromium telepítő mód
- Normál indítás → WinForms alkalmazás

Az `[STAThread]` attribútum kötelező WinForms alkalmazásoknál, mert a Windows UI szálmodellje ezt igényli (Single-Thread Apartment).

---

### MainForm.cs

A főablak. Fő felelősségei:

**UI vezérlők kezelése:**
- Fájlválasztó (`OpenFileDialog`)
- Folyamatjelző frissítése (`ProgressBar`)
- Gombállapotok váltása futás közben

**Szünet/Folytatás mechanizmusa:**

A szünet egy `SemaphoreSlim(1,1)` segítségével működik:
```
Szünet gomb → _szunetel = true, semaphore.Wait(0) [elveszi a tokent]
Futási ciklus → if (_szunetel) semaphore.WaitAsync(ct) [megáll itt]
Folytatás gomb → semaphore.Release() [felengedi, a ciklus folytatódik]
```
Ez azért jó, mert a keresés sosem szakad meg egy lekérdezés közepén — mindig a keresések **között** áll meg.

**Thread-safety:**
Minden UI frissítés (`SetStatusz`, `SetProgress` stb.) `InvokeRequired` ellenőrzéssel van védve, mert a Playwright aszinkron műveletei háttérszálon futnak, és a WinForms vezérlőket csak a UI szálról szabad módosítani.

**Excel mentés leállításkor:**
Az `allResults` lista az egész futás alatt gyűjti az eredményeket. A mentés egy belső `finally` blokkban történik, így leállításkor is lefut.

---

### OszlopValasztoDialog.cs

Párbeszédablak az Excel oszlopok hozzárendeléséhez.

**Működés:**
1. Megnyitáskor beolvassa az Excel fájl első néhány sorát (ClosedXML-lel)
2. Az oszlopneveket a fejléc tartalom alapján mutatja: `B – Helyrajzi szám`
3. Az előnézet panel mutatja az első 3 sor adatait a kiválasztott oszlopok alapján
4. OK-ra az indexeket (1-alapú) visszaadja a `MainForm`-nak

**Fontos részlet — inicializálási sorrend:**
A `_lblElonezet` label-t **előbb** kell létrehozni mint a ComboBox `SelectedIndex` értékét beállítani, mert a `SelectedIndexChanged` esemény azonnal meghívja a `FrissitElonezet()` metódust, ami a label-t használja. Ha fordított sorrendben történne, `NullReferenceException` lenne az eredmény.

---

### OenyScraperService.cs

Ez a program "lelke" — a Playwright-alapú böngésző-automatizálás.

**Inicializálás (`InitAsync`):**
- Elindítja a Chrome böngészőt (ha nincs Chrome, Chromium-ra esik vissza)
- Regisztrál egy globális Response figyelőt az EOV koordinátákhoz
- Betölti az OÉNY kezdőlapját
- Elfogadja a cookie bannert (ha megjelenik)

**Chrome keresése (`FindChrome`):**
Három helyen keresi a Chrome-ot (Program Files, Program Files x86, LocalAppData). Ha egyik sem található, `null`-t ad vissza és a Playwright a saját Chromium-át használja.

**Autocomplete kezelés (`AutocompleteKivalasztAsync`):**
Az OÉNY oldal PrimeNG autocomplete komponenseket használ. A kezelés menete:
1. Beírja az értéket a mezőbe
2. Megvárja a dropdown megjelenését (több szelektort próbál, mert Chrome-ban az Angular overlay máshova renderel)
3. Pontos szöveges egyezést keres a dropdown elemek közt
4. Ha talál egyezést → JS-sel kattint rá (megbízhatóbb mint a Playwright `.Click()`)
5. Ha nincs egyezés → `false`-t ad vissza, a keresés hibával végződik

**Miért JS kattintás és nem Playwright Click?**
A PrimeNG dropdown elemek Angular-ban renderelnek, és a Playwright `.ClickAsync()` néha "elvéti" az elemet mert az overlay z-index problémákat okoz. A `el.click()` JavaScript hívás közvetlenül a DOM-on hajtódik végre, ami megbízhatóbb.

**EOV koordináták kinyerése:**
Két különböző API végpontból jönnek a koordináták, a keresési módtól függően:

- **HRSZ mód:** `GET /hk-api/parcels/bounding-box?id={parcelId}`
  - Válasz: `{ "boundingBox": { "min": {"x":..,"y":..}, "max": {"x":..,"y":..} } }`
  - A program a min és max átlagát számítja (telek középpontja)

- **Cím mód:** `GET /hk-api/addresses/position?id={addressId}`
  - Válasz: `{ "point": {"x":..,"y":..} }`
  - Közvetlenül a koordináta, nem kell számítani

A globális `_page.Response` esemény mindkét végpontot figyeli és feltölti `_eovX`/`_eovY` mezőket.

**Találati kártyák kinyerése (`ExtractCardsAsync`):**
Az OÉNY eredményoldala `div.result-card` elemekben mutatja a találatokat. Minden kártyából:
- `.result-card-address` → cím
- `.result-card-hrsz` → HRSZ (a "HRSZ:" prefix eltávolítva)
- `div.result-card-parcel` → albetét szám (regex: `(\d+)\s*db`)
- `a.property-sheet-navigation-link` → tulajdoni lap link (JS-sel kinyerve, mert Angular lazy rendereli)

---

### ExcelService.cs

**`ReadInput(filePath, varosOszlop, hrszOszlop, cimOszlop)`:**
- Az oszlop indexek 1-alapúak (1=A, 2=B, ...)
- 0 = az oszlop nincs megadva
- Minden sort feldolgoz az első sortól (nincs automatikus fejléc-kihagyás — a felhasználó maga dönti el az Oszlopok párbeszédben)
- Üres város vagy mindkét keresési mező üres esetén kihagyja a sort

**`WriteResults(filePath, results)`:**
- A tulajdoni lap linket `Uri.UnescapeDataString()`-gel dekódolja a `SetHyperlink` előtt, mert a ClosedXML nem kezeli jól a `%C3%BC` típusú URL-kódolást
- Váltakozó háttérszín (AliceBlue) a különböző bemeneti sorokhoz tartozó találatcsoportoknál
- Piros háttér a hibás soroknál

---

### Models.cs

Két egyszerű `record` típus:

**`InputRow(Varos, Hrsz, Cim)`:**  
Egy bemeneti Excel sort reprezentál. A `record` típus immutable (nem módosítható), ami biztonságosabbá teszi a párhuzamos feldolgozást.

**`SearchResult`:**  
Egy találati kártyát reprezentál. Init-only property-ekkel, így a létrehozás után nem módosítható.

---

## 6. Adatfolyam

```
bemenet.xlsx
    │
    ▼
ExcelService.ReadInput()
    │  List<InputRow>
    ▼
foreach (InputRow)
    │
    ├─ HRSZ mód ──► OenyScraperService.SearchByHrszAsync()
    │                    │
    └─ Cím mód ──► OenyScraperService.SearchByCimAsync()
                         │
                    AutocompleteKivalasztAsync() × 2
                    (Település + HRSZ/Cím)
                         │
                    API Response figyelő
                    → _eovX, _eovY feltöltése
                         │
                    ExtractCardsAsync()
                    → List<SearchResult>
                         │
    ◄────────────────────┘
    │
    ▼
ExcelService.WriteResults()
    │
    ▼
eredmenyek.xlsx
```

---

## 7. API végpontok

Az OÉNY oldal a következő belső REST API-t használja (nem publikus, de a böngésző hálózati kéréseiből kinyerhető):

| Végpont | Mikor hívódik | Mit ad vissza |
|---|---|---|
| `GET /hk-api/settlements/search?searchString=...` | Település gépeléskor | Egyező települések listája |
| `GET /hk-api/settlements/bounding-box?kshCode=...` | Település kiválasztásakor | A település határolótéglalap |
| `GET /hk-api/parcels/search?kshCode=...&lotNumber=...` | HRSZ gépeléskor | Egyező HRSZ-ek listája |
| `GET /hk-api/parcels/bounding-box?id=...` | HRSZ kiválasztásakor | **EOV koordináták** (HRSZ mód) |
| `GET /hk-api/addresses/search?kshCode=...&searchString=...` | Cím gépeléskor | Egyező címek listája |
| `GET /hk-api/addresses/position?id=...` | Cím kiválasztásakor | **EOV koordináták** (Cím mód) |

**Fontos:** ezek nem publikus API végpontok, Anthropic garanciát nem vállal arra, hogy változatlanok maradnak. Ha az oldal frissül és a végpontok megváltoznak, a `_page.Response` figyelőt kell frissíteni az `OenyScraperService.InitAsync()`-ban.

---

## 8. Konfiguráció és hangolás

### ScraperConfig (MainForm.cs → FuttatasAsync)

```csharp
var config = new ScraperConfig
{
    Headless               = !_chkLathato.Checked, // true = láthatatlan böngésző
    SlowMo                 = 0,       // ms késleltetés minden Playwright lépés közt
                                      // 0 = nincs, 200+ = debugoláshoz
    DelayBetweenSearchesMs = 500,     // ms várakozás keresések között
                                      // Csökkentsd ha gyorsabb kell,
                                      // növeld ha az OÉNY szerver hibázik
    NavigationTimeoutMs    = 30_000,  // Oldalbetöltési timeout (ms)
    ElementTimeoutMs       = 10_000,  // DOM elem várakozási timeout (ms)
};
```

### Várakozási idők az OenyScraperService-ben

Ha az oldal lassan tölt vagy a keresések hibáznak, ezeket lehet növelni:

| Hol | Jelenlegi érték | Növeld ha... |
|---|---|---|
| `AutocompleteKivalasztAsync` FillAsync után | 800ms | Dropdown nem jelenik meg |
| `AutocompleteKivalasztAsync` kattintás után | 100ms | Következő lépés hibázik |
| `SearchByHrszAsync` fül kattintás után | 300ms | HRSZ mező nem jelenik meg |
| `WaitForApiResponseAsync` timeout | 5000ms | EOV koordináta hiányzik |
| `ExtractCardsAsync` Angular render | 400ms | Tulajdoni lap link hiányzik |

---

## 9. Hibaelhárítás

### "Dropdown nem jelent meg" hiba

**Ok:** Chrome lassabban rendereli az Angular komponenseket mint a Chromium.  
**Megoldás:** Növeld az `AutocompleteKivalasztAsync`-ban a `WaitForTimeoutAsync(800)` értékét 1200-ra.

### "Nincs pontos egyezés" hiba

**Ok:** A dropdown más szöveget mutat mint amit a bemenetben adtál meg.  
**Megoldás:** A konzolban láthatók a `DROPDOWN ELEM:` sorok — nézd meg mi jelenik meg pontosan. Pl. ha `"Budapest XIV."` jelenik meg `"Budapest"` helyett, pontosítani kell a bemeneti értéket.

### EOV koordináta hiányzik

**Ok:** A `bounding-box` vagy `addresses/position` API válasz nem érkezett meg időben.  
**Megoldás:** Növeld a `WaitForApiResponseAsync` timeout értékét 8000-re.

### Tulajdoni lap link hiányzik

**Ok:** Az Angular `app-property-sheet-navigation-link` komponens nem renderelte még a linket.  
**Megoldás:** Növeld az `ExtractCardsAsync` elején lévő `WaitForTimeoutAsync(400)` értékét 1000-re.

### Chrome nem találja az oldalt (SSL hiba)

**Ok:** Céges proxy/tűzfal megszakítja a HTTPS kapcsolatot.  
**Megoldás:** Az `IgnoreHTTPSErrors = true` már be van állítva, de ha ez sem elég, a proxy beállításait kell felülvizsgálni.

### "0xe0434352" hibakód indításkor

**Ok:** Nem kezelt kivétel az alkalmazás indításakor.  
**Megoldás:** A `Program.cs`-ben lévő `try/catch` MessageBox-ban mutatja a pontos hibát.

---

## 10. Továbbfejlesztési lehetőségek

### Könnyen megvalósítható fejlesztések

- **Több találat kártyánként eltérő koordináta:** jelenleg minden kártyához ugyanaz az EOV koordináta kerül (az első találaté). Kártyánként külön kattintással ez javítható.
- **CSV kimenet** az Excel mellé
- **Naplózás fájlba** a konzolos log helyett
- **Beállítások mentése** (utolsó mappa, oszlop beállítások) — `Properties.Settings` vagy JSON fájl

### Közepes komplexitású fejlesztések

- **Párhuzamos keresés** — több böngészőpéldánnyal egyszerre több sort feldolgozni (figyelni kell a szerver terhelésre)
- **Ismételt próbálkozás** hibás soroknál (retry logic)
- **Részleges futtatás** — csak a hibás sorokat futtassa újra egy korábbi eredményfájl alapján

### Nagyobb fejlesztések

- **Az OÉNY API közvetlen hívása** a Playwright helyett — ha sikerül a session cookie-t megszerezni, a REST API-t közvetlenül lehetne hívni `HttpClient`-tel, ami sokkal gyorsabb lenne
- **Ütemezés** — automatikus futtatás adott időpontban

---

## Függelék: DOM szelektorok összefoglalója

Az OÉNY oldal Angular/PrimeNG alapú SPA. Ha az oldal frissül és a szelektorok megváltoznak, ezeket kell ellenőrizni:

| Mit keres | Szelektor | Hol van |
|---|---|---|
| Település input | `input.p-autocomplete-input` `.Nth(0)` | Főoldal |
| HRSZ input | `input.p-autocomplete-input` `.Nth(1)` | HRSZ fül kiválasztása után |
| Cím input | `input.p-autocomplete-input:visible` `.Last` | Cím fül |
| Dropdown elemek | `li.p-autocomplete-item` | Gépelés után |
| Helyrajzi szám fül | `span:has-text('Helyrajzi szám')` | Keresési mód váltó |
| Találati kártya | `div.result-card` | Eredmény panel |
| Cím a kártyán | `.result-card-address` | Kártyán belül |
| HRSZ a kártyán | `.result-card-hrsz` | Kártyán belül |
| Albetétek | `div.result-card-parcel` | Kártyán belül |
| Tulajdoni lap | `a.property-sheet-navigation-link` | Kártyán belül (Angular lazy) |
