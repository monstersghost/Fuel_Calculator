"""Build the data files the app embeds: world borders, country currencies, FX snapshot and seed fuel prices.

Inputs are downloaded by hand (see tools/data/README.md) into one folder and passed with --inputs.
Outputs are written into src/FuelCalculator.Core. Run with the standard library only:

    py -3 -I tools/data/build_world_data.py --inputs <download folder> --checked 2026-11-27
"""
import argparse
import csv
import datetime as dt
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xlsx_lite  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CORE = os.path.join(REPO, "src", "FuelCalculator.Core")
FUELS = ("GASOLINE_91", "GASOLINE_95", "GASOLINE_98", "DIESEL")

# Natural Earth codes / World Bank codes that the ISO table lacks.
EXTRA_CURRENCIES = {"XK": "EUR", "PS": "ILS"}
EXTRA_ISO3 = {"XKX": "XK", "KSV": "XK"}


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"wrote {os.path.relpath(path, REPO)} ({os.path.getsize(path) / 1024:.0f} KB)")


def excel_date(serial):
    return (dt.date(1899, 12, 30) + dt.timedelta(days=int(float(serial)))).isoformat()


def as_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


# ---------- borders ----------
def build_borders(geojson_path):
    with open(geojson_path, encoding="utf-8") as f:
        data = json.load(f)

    countries = []
    for feature in data["features"]:
        props = feature["properties"]
        code = next((props.get(k) for k in ("ISO_A2_EH", "ISO_A2", "WB_A2")
                     if props.get(k) and props.get(k) != "-99" and len(props.get(k)) == 2), None)
        geom = feature["geometry"]
        if code is None or geom is None:
            continue
        polys = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]
        rings = [[[round(v, 4) for pt in ring for v in (pt[0], pt[1])] for ring in poly] for poly in polys]
        countries.append({"code": code.upper(), "name": props.get("NAME"), "polygons": rings})

    countries.sort(key=lambda c: c["code"])
    write_json(os.path.join(CORE, "Segmentation", "Data", "world-countries.json"), {
        "source": "Natural Earth 1:50m Admin 0 - Countries (public domain), https://www.naturalearthdata.com/",
        "countries": countries,
    })
    return {c["code"] for c in countries}


# ---------- countries and currencies ----------
def build_countries(country_codes_csv):
    iso3_to_iso2 = dict(EXTRA_ISO3)
    countries = {}
    with open(country_codes_csv, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            a2, a3 = row["ISO3166-1-Alpha-2"].strip(), row["ISO3166-1-Alpha-3"].strip()
            currency = row["ISO4217-currency_alphabetic_code"].split(",")[0].strip()
            if len(a2) != 2:
                continue
            iso3_to_iso2[a3] = a2
            countries[a2] = {"name": row["CLDR display name"].strip() or row["official_name_en"].strip(),
                             "currency": currency or None}
    for a2, currency in EXTRA_CURRENCIES.items():
        entry = countries.setdefault(a2, {"name": a2, "currency": None})
        entry["currency"] = entry["currency"] or currency
    write_json(os.path.join(CORE, "Domain", "Data", "countries.json"), {
        "source": "datasets/country-codes (ODC-PDDL), https://github.com/datasets/country-codes",
        "countries": countries,
    })
    return countries, iso3_to_iso2


# ---------- FX ----------
def build_fx(fx_json):
    with open(fx_json, encoding="utf-8") as f:
        data = json.load(f)
    if data.get("result") != "success":
        raise SystemExit("FX snapshot is not a successful response")
    as_of = dt.datetime.strptime(data["time_last_update_utc"], "%a, %d %b %Y %H:%M:%S %z").date().isoformat()
    write_json(os.path.join(CORE, "Currency", "Data", "fx-usd.json"), {
        "source": "Rates By Exchange Rate API, https://www.exchangerate-api.com",
        "asOf": as_of,
        "base": "USD",
        "rates": data["rates"],
    })
    return as_of


# ---------- prices ----------
def curated_rows(path):
    with open(path, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            yield {
                "country": row["country"], "fuel": row["fuel"], "price": float(row["price"]),
                "currency": row["currency"], "effective": row["effective"], "confidence": row["confidence"],
                "source": row["source"], "url": row["url"],
            }


def eu_rows(path):
    sheet = xlsx_lite.sheets(path)[0][1]
    rows = list(xlsx_lite.rows(path, sheet))
    effective = excel_date(rows[1][0])
    names = {}
    for row in rows[2:]:
        name = (row[0] or "").strip()
        if not name or name.startswith("EU") or name.lower().startswith("euro"):
            continue
        names[name] = row
    return effective, names


EU_NAMES = {
    "Austria": "AT", "Belgium": "BE", "Bulgaria": "BG", "Croatia": "HR", "Cyprus": "CY", "Czechia": "CZ",
    "Denmark": "DK", "Estonia": "EE", "Finland": "FI", "France": "FR", "Germany": "DE", "Greece": "GR",
    "Hungary": "HU", "Ireland": "IE", "Italy": "IT", "Latvia": "LV", "Lithuania": "LT", "Luxembourg": "LU",
    "Malta": "MT", "Netherlands": "NL", "Poland": "PL", "Portugal": "PT", "Romania": "RO", "Slovakia": "SK",
    "Slovenia": "SI", "Spain": "ES", "Sweden": "SE",
}


def build_eu(path):
    effective, rows = eu_rows(path)
    out = []
    for name, row in rows.items():
        code = EU_NAMES.get(name)
        if code is None:
            continue
        for column, fuel in ((1, "GASOLINE_95"), (2, "DIESEL")):
            per_1000 = as_float(row[column] if len(row) > column else None)
            if per_1000 and per_1000 > 0:
                out.append({"country": code, "fuel": fuel, "price": round(per_1000 / 1000, 4), "currency": "EUR",
                            "effective": effective, "confidence": "HIGH",
                            "source": "EU Weekly Oil Bulletin (prices with taxes)",
                            "url": "https://energy.ec.europa.eu/data-and-analysis/weekly-oil-bulletin_en"})
    missing = sorted(set(EU_NAMES) - set(rows))
    if missing:
        print("EU bulletin rows not found:", missing)
    return out


WB_SHEETS = {
    "GASOLINE_91": "Reg Gasoline (below RON 95) USD",
    "GASOLINE_95": "Premium Gasoline RON95or ab USD",
    "DIESEL": "Diesel USD",
}


def build_world_bank(path, iso3_to_iso2):
    sheets = dict(xlsx_lite.sheets(path))
    out = []
    for fuel, sheet_name in WB_SHEETS.items():
        rows = list(xlsx_lite.rows(path, sheets[sheet_name]))
        header = rows[0]
        months = [i for i, v in enumerate(header) if as_float(v) and as_float(v) > 40000]
        latest = {}
        for row in rows[1:]:
            if len(row) < 3 or not row[2]:
                continue
            code = iso3_to_iso2.get(row[2].strip())
            if code is None:
                continue
            for i in reversed(months):
                value = as_float(row[i] if i < len(row) else None)
                if value is not None and 0.005 < value < 10:
                    latest.setdefault(code, []).append((float(header[i]), value))
                    break
        for code, points in latest.items():
            newest = max(serial for serial, _ in points)
            value = statistics.median(v for s, v in points if s == newest)
            out.append({"country": code, "fuel": fuel, "price": round(value, 4), "currency": "USD",
                        "effective": excel_date(newest), "confidence": "LOW",
                        "source": "World Bank Global Fuel Prices Database (Apr 2025 release)",
                        "url": "https://datacatalog.worldbank.org/search/dataset/0066829/global-fuel-prices-database"})
    return out


STALE_AGAINST_CURATED_DAYS = 180


def build_prices(curated, eu, world_bank, checked):
    # A country with a recent curated price should not mix in a much older World Bank grade (it can make
    # premium look cheaper than regular); the calculator's nearest-grade fallback covers the gap instead.
    newest_curated = {}
    for row in curated:
        newest_curated[row["country"]] = max(newest_curated.get(row["country"], ""), row["effective"])

    def too_old(row):
        newest = newest_curated.get(row["country"])
        return newest is not None and (dt.date.fromisoformat(newest) - dt.date.fromisoformat(row["effective"])).days > STALE_AGAINST_CURATED_DAYS

    chosen = {}
    for layer in (curated, eu, [row for row in world_bank if not too_old(row)]):  # first layer to price a (country, fuel) wins
        for row in layer:
            chosen.setdefault((row["country"], row["fuel"]), row)
    prices = sorted(chosen.values(), key=lambda r: (r["country"], FUELS.index(r["fuel"])))
    write_json(os.path.join(CORE, "Fuel", "Data", "seed-prices.json"), {
        "checkedOn": checked,
        "note": "Priority: curated (tools/data/curated-prices.csv) > EU Weekly Oil Bulletin > World Bank.",
        "prices": prices,
    })
    return prices


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--inputs", required=True)
    parser.add_argument("--checked", required=True, help="Date the sources were checked, YYYY-MM-DD")
    args = parser.parse_args()
    i = lambda name: os.path.join(args.inputs, name)  # noqa: E731

    border_codes = build_borders(i("ne_50m_admin_0_countries.geojson"))
    countries, iso3_to_iso2 = build_countries(i("country-codes.csv"))
    fx_date = build_fx(i("fx_usd.json"))
    prices = build_prices(
        list(curated_rows(os.path.join(os.path.dirname(os.path.abspath(__file__)), "curated-prices.csv"))),
        build_eu(i("eu_wob_taxes.xlsx")),
        build_world_bank(i("wb_fuel.xlsx"), iso3_to_iso2),
        args.checked)

    priced = {p["country"] for p in prices}
    by_conf = {}
    for p in prices:
        by_conf[p["confidence"]] = by_conf.get(p["confidence"], 0) + 1
    print(f"borders: {len(border_codes)} countries; currencies: {len(countries)}; FX as of {fx_date}")
    print(f"prices: {len(prices)} rows for {len(priced)} countries; by confidence {by_conf}")
    print("countries on the map with no price:", len(border_codes - priced))
    print("no currency for map countries:", sorted(c for c in border_codes if not (countries.get(c) or {}).get("currency")))


if __name__ == "__main__":
    main()
