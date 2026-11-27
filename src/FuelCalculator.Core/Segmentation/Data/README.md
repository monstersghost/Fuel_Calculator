# Country border data

`region-countries.json` holds the country borders `PolygonCountryResolver` uses to work out which
country each part of a route is in.

- **Source:** Natural Earth, 1:50m Admin 0 – Countries
  (`ne_50m_admin_0_countries.geojson` from https://github.com/nvkelso/natural-earth-vector).
  Natural Earth is public domain; credit "Made with Natural Earth" is appreciated but not required.
- **Downloaded:** 2026-11-27.
- **Cut to:** longitude 24–64, latitude 11–43 (Egypt and Turkey to Pakistan, Yemen to the Caucasus).
  Only polygons that touch that box are kept. Coordinates are rounded to 4 decimals (about 11 m).
- **Format:** `countries[].polygons[]` is a list of rings: the outer ring first, then holes. Each ring
  is a flat `[lon, lat, lon, lat, ...]` array. Codes are ISO 3166-1 alpha-2 (`ISO_A2_EH`).
- **Accuracy:** 1:50m borders are within roughly 1 km of the real line. That is fine for splitting a
  route's distance by country, but not for deciding which side of a border a single building is on.
  A point that falls in no polygon (a causeway, a coastal road cut off by the simplified coastline)
  takes the nearest country within 25 km.

## Regenerating

The extraction script is short: load the GeoJSON, keep features whose polygons touch the box, take
`ISO_A2_EH` (falling back to `ISO_A2`), flatten and round each ring, and write the shape above. To
widen the region, change the box and rerun the script, then run the tests. `C2 border towns resolve
to the right country` checks a set of border towns.
