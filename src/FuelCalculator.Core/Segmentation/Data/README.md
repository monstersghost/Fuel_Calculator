# Country border data

`world-countries.json` holds the country borders `PolygonCountryResolver` uses to work out which country each part of
a route is in. It is built by `tools/data/build_world_data.py`; see `tools/data/README.md`.

- **Source:** Natural Earth, 1:50m Admin 0 – Countries, 237 countries (`ne_50m_admin_0_countries.geojson` from
  https://github.com/nvkelso/natural-earth-vector). Natural Earth is public domain; credit "Made with Natural Earth"
  is appreciated but not required.
- **Downloaded:** 2026-11-27.
- **Format:** `countries[].polygons[]` is a list of rings, outer ring first and then holes. Each ring is a flat
  `[lon, lat, lon, lat, ...]` array rounded to 4 decimals (about 11 m). Codes are ISO 3166-1 alpha-2 (`ISO_A2_EH`).
- **Accuracy:** 1:50m borders are within roughly 1 km of the real line. That is fine for splitting a route's distance
  by country, but not for deciding which side of a border a single building is on. A point that falls in no polygon
  (a causeway, or a coastal road cut off by the simplified coastline) takes the nearest country within 25 km.
