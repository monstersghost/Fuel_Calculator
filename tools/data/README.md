# Data build

`build_world_data.py` builds the data files embedded in `FuelCalculator.Core`. It uses only the Python standard
library; `xlsx_lite.py` reads the Excel inputs.

| Output | Used by | Built from |
|---|---|---|
| `Segmentation/Data/world-countries.json` | `PolygonCountryResolver` | Natural Earth 1:50m Admin 0 countries (237 countries) |
| `Domain/Data/countries.json` | `CountryCatalog` (name, currency) | datasets/country-codes (ISO 3166 + ISO 4217) |
| `Currency/Data/fx-usd.json` | `StaticCurrencyConverter` | Exchange Rate API open endpoint, 166 currencies per USD |
| `Fuel/Data/seed-prices.json` | `StaticSeedFuelPriceProvider` | `curated-prices.csv` > EU Weekly Oil Bulletin > World Bank (first source to price a country and grade wins) |

## Rebuild

1. Download the inputs into one empty folder, keeping these file names:

   | File | URL | Licence |
   |---|---|---|
   | `ne_50m_admin_0_countries.geojson` | https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson | Public domain |
   | `country-codes.csv` | https://raw.githubusercontent.com/datasets/country-codes/main/data/country-codes.csv | ODC-PDDL |
   | `fx_usd.json` | https://open.er-api.com/v6/latest/USD | Free, attribution required: show "Rates By Exchange Rate API" with a link |
   | `eu_wob_taxes.xlsx` | "Prices with taxes, latest prices (xlsx)" on https://energy.ec.europa.eu/data-and-analysis/weekly-oil-bulletin_en (the link changes each week) | © European Union, reuse allowed |
   | `wb_fuel.xlsx` | https://datacatalogfiles.worldbank.org/ddh-published/0066829/DR0095290/Global_Fuel_Prices_Database.xlsx | ODbL |

2. Update `curated-prices.csv`. Every row needs a source and a URL. Grades: `GASOLINE_91` is any regular grade below
   RON 95 (US regular, octane 90/92/93), `GASOLINE_95` is RON 95, `GASOLINE_98` is RON 97–98, and `DIESEL` is road diesel.
   Use `HIGH` only for an official price list, `MEDIUM` for a reliable report of one, and `LOW` for anything else.

3. Run the build and the tests:

   ```powershell
   py -3 -I tools\data\build_world_data.py --inputs <download folder> --checked 2026-11-27
   dotnet run --project tests\FuelCalculator.Tests\FuelCalculator.Tests.csproj
   ```

   The script prints the coverage: countries priced, rows by confidence, and map countries with no price.

## Rules worth knowing

- The EU bulletin gives Euro-super 95 and road diesel in EUR per 1,000 litres, with taxes, for the data week in its
  header. It is converted to per litre.
- The World Bank file ends in April 2025 and its USD sheets are already per litre. For each country and grade, the
  latest month is taken, using the median when several rows (cities or products) share that month. These rows are LOW.
- If a country has a curated price, World Bank rows more than 180 days older than it are dropped. Otherwise an old
  premium price can look cheaper than a current regular one; the calculator's nearest-grade fallback fills the gap.
- The calculator warns about any seed price more than six months old.

## Coverage on 2026-11-27

- 151 countries priced, 349 rows:
  - HIGH: 64 (EU bulletin, Aramco, UAE committee, South Africa).
  - MEDIUM: 33.
  - LOW: 252, mostly the World Bank's 2025 data.
- 86 map countries have no price. Most are territories and small islands; the larger ones are Chad, Afghanistan,
  Morocco, Turkmenistan, Somalia, Yemen, Guinea, Guyana, Tajikistan, North Korea, Suriname, Eritrea, Cuba and Western Sahara.
