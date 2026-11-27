# Fuel Calculator: review and improvement proposal

Date: 2026-11-27. Scope: fuel stops, border crossings and a better way to get prices.
Status: proposal. Step 1 (C2–C8 and the hygiene items) is done on branch `fix/correctness-pass`
(2026-11-27). C1 and steps 2–6 are open.

## 1. What the app is today

About 1,300 lines of C# plus one React page (`frontend/src/main.jsx`).
The build is clean and all 11 console tests pass.

Request flow for `POST /api/trips/estimate` (`src/FuelCalculator.Api/Program.cs`):

1. Validate the input. A Google Maps link is expanded and parsed into origin, waypoints and destination.
2. Get the route: `MockRouteProvider` by default; `GoogleRoutesProvider` when `RouteProvider:Mode=Google`.
3. Split the route by country (`PolylineCountrySegmenter`). It takes a sample every 5 km and asks
   `StaticBoundingBoxCountryResolver` which country the midpoint is in.
4. Price lookup: manual override first, then the hard-coded seed table (KW, SA, QA, AE, BH, OM).
5. Calculation: litres burned in each country × that country's price, then FX into the output currency.
6. Fuel stops: `ceil((distance − starting range) / full-tank range)`. This gives a count only.

The provider interfaces (ADR-005) are a good base. Everything below fits behind them.

## 2. Problems found

### Correctness

| # | Problem | Where | Effect |
|---|---|---|---|
| C1 | The model is wrong: fuel is charged where it is burned, not where it is bought. | `TripFuelCostCalculator` | Kuwait→UAE is priced as if you paid UAE prices for the UAE km. In reality you fill up cheap in KW/SA and arrive with fuel in the tank. This is the main reason fuel stops matter. |
| C2 | The country boxes are rectangles that overlap, and the first match wins. | `StaticBoundingBoxCountryResolver` | Al Hadithah (Saudi side of the Jordan border) and the Saudi north fall inside the JO box. Khafji (SA) falls inside the KW box. Irbid (JO) and western Iraq (Al-Qa'im) fall inside the SY box. Iraq and Lebanon are missing entirely. Kuwait→Syria assigns Saudi km to Jordan. |
| C3 | Missing prices count as 0 in the total. | `CalculateAsync`: `ConvertedCost ?? 0m` | The total is understated, and the only sign is a warning. |
| C4 | A blank currency on a manual price defaults to the *output* currency. The contract defaults it to `KWD`. | `ValidateTripRequestAsync` | A SAR price entered with no currency is read as KWD (about 12× too high). |
| C5 | The seed `FetchedAt` is set to `UtcNow` at startup. | `StaticSeedFuelPriceProvider` | Old seed data looks freshly fetched. |
| C6 | Trip duration is computed but never returned. | `TripEstimateResult` | There is nothing to add border time to, and no arrival time. |
| C7 | Fuel type availability is not modelled. SA does not sell 98, so you only get "missing price". | Seed / calculator | A 98-octane car gets a vague warning instead of "use 95 in SA". |
| C8 | The fuel stop count has no reserve margin. It plans to run the tank to 0%. | `CreateFuelStopEstimate` | Unsafe on desert stretches. |

### Hygiene

- CORS is `AllowAnyOrigin`, and a route failure returns `ex.Message` to the client.
- The short-link resolver follows redirects to any host. It is low risk, but limit it to Google hosts.
- Tests are a hand-rolled console runner with no API or UI coverage. The backlog already plans xUnit and Playwright.
- `api-dev.log` and `frontend-dev.log` are committed to the repo root.

## 3. Fuel stops: plan where you buy, not just how many

### Model

Simulate the tank along the route instead of dividing distance by range.

- State: position (km), litres in tank, current country.
- Inputs:
  - tank size
  - starting %
  - **reserve %** (default 15%)
  - **minimum fill** (no stopping for 3 litres)
  - optional **"arrive with at least X%"**
- Candidate stations come from the station source below. Each one has a km position on the route and a country, and therefore a price.
- Algorithm: the classic gas-station problem, which is greedy and optimal for a fixed route:
  - At each station, look ahead within reachable range (full tank minus reserve).
  - If a cheaper station is reachable, buy only enough to get there.
  - Otherwise fill up.
  - Equal prices within a country give "fill up as late as is safe", which matches how people drive.
- Cost = sum of (litres bought × local price) + the value of the starting fuel (priced at the origin price and shown on its own line). Optionally subtract the value of the fuel left on arrival.

### What you see

```
Stop 1  km 260  Al Khafji (SA)   38 L × 2.33 SAR  = 88.5 SAR
Stop 2  km 690  Al Hofuf (SA)    44 L × 2.33 SAR  = 102.5 SAR   ← fill up: UAE is 27% dearer
Border  km 1010 Al Batha → Ghuwaifat   est. 1h30 (Thu evening peak)
Arrive  Dubai with 41% tank
```

Also show:
- **Longest gap between stations**, with a warning when it exceeds the safe range.
- **Saving vs. naive** ("buying in SA instead of AE saved 18 KWD").

### Where stations come from

| Option | Coverage | Cost | Notes |
|---|---|---|---|
| **OSM `amenity=fuel`** through Overpass, pre-extracted for the region into a local file / PostGIS table | Good on GCC highways | Free (ODbL, needs attribution) | Recommended base. Refresh monthly; snap stations to the route within ~1 km. |
| Google Places API (New) Text Search with search-along-route (pass the route polyline) | Very good | Paid per request | Good for the final list, names and opening hours. The `fuelOptions` field has per-station prices, but GCC coverage is unconfirmed; probe it before relying on it. |
| No station data | n/a | n/a | Fallback: place "virtual stops" by range and warn that they are approximate. |

Interfaces:
- `IFuelStationProvider.GetStationsAlongRouteAsync(route, corridorKm)`
- `IRefuelPlanner.Plan(route, segments, stations, prices, tank)`

The planner is pure and deterministic, so it is easy to unit test.

## 4. Border crossings

### Detection

The segmenter already knows where the country changes.
- Store the km and position of each change.
- Snap it to the nearest crossing in a curated `border-crossings.json`, cross-checked against OSM `barrier=border_control`.

Example entries:
- Nuwaiseeb / Al Khafji (KW–SA)
- Salwa / Abu Samra (SA–QA)
- Al Batha / Ghuwaifat (SA–AE)
- King Fahd Causeway (SA–BH)
- Hili, Khatm Al Shikla (AE–OM)
- Al Hadithah / Al Omari (SA–JO)
- Jaber / Nassib (JO–SY)
- Abdali / Safwan (KW–IQ)

### Data per crossing (versioned, with a "last verified" date)

- Typical delay by direction and time band: weekday, Thursday/Friday evening, Eid and holidays.
  Published guidance mentions multi-hour queues at peaks on Nuwaiseeb, Al Batha and the Causeway.
- Opening hours (most are 24h).
- Tolls, e.g. the King Fahd Causeway toll.
- Required documents and insurance:
  - Saudi Arabia requires Manafith insurance for foreign-plated cars, sold at the crossing.
  - The Arab Orange Card applies outside the GCC.
  - Visa notes.
- Fuel rules, such as no jerrycans.

### Effect on the trip

- Border delay is added to the route duration, so you get an **arrival time**. This needs C6 fixed first. Use Google Routes `TRAFFIC_AWARE` with a `departureTime` for the driving part.
- A departure time picker shows "leave at 06:00 Thu: est. border 45 min; leave 18:00 Thu: est. 2h30".
- Costs at the border (tolls, insurance) appear as their own line items next to fuel.
- Live delays are nice to have:
  - The King Fahd Causeway publishes live crossing times on its site and in the JESR app, but I found no public API.
  - None of the other crossings has a live feed.
  - Keep an `IBorderDelayProvider` with a static implementation now and leave room for a live one later.

## 5. Getting prices properly

Key fact: **GCC retail fuel prices are set by governments**, not by stations. They change at most monthly:
- UAE, Qatar, Oman: monthly announcements.
- Kuwait: fixed, or quarterly.
- Saudi Arabia: Aramco reviews.
- Bahrain: policy-driven.

So "live per-station prices" are mostly unnecessary in the region. What you need is a **dated price table that is updated on time**.

### Recommended chain (first hit wins; every quote carries source, effective date and fetched time)

1. **Manual override**: already exists.
2. **Official regulated price table** (`fuel_prices` table: country, fuel type, price, currency, `effective_from`, `effective_to`, source URL, verified by).
   - A monthly job runs on the 1st (UAE and Qatar announce on the last day of the month).
   - It either pulls the official announcement where a stable machine-readable page exists, or opens an "update prices" task.
   - A small admin page edits the table. The calculator picks the row valid on the **travel date**, not today.
   - One adapter per country, replacing the empty `SaudiAramcoFuelPriceProvider`, `AdnocFuelPriceProvider` and `QatarFuelPriceProvider` stubs, all writing into this table.
3. **GlobalPetrolPrices licensed feed** for countries outside the GCC (JO, SY, IQ, LB, TR, …):
   - 135 countries, monthly updates, XML or Excel.
   - Price by quote; a 2-week free trial is available.
   - Licence it; don't scrape it (ADR-005 already rejects scraping).
4. **Seed table**: last resort, marked LOW confidence, with its real date (fix C5).

Extras:
- **Staleness rules**:
  - A price older than its country's update cycle is downgraded to LOW confidence and flagged in the UI ("UAE price is from Oct 2026; Dec price not loaded").
  - The total is shown as a **range** when any confidence is LOW.
- **Travel date matters**: a trip planned for next month in the UAE should warn that the price changes on the 1st.
- **Fuel grade mapping**: each country lists the grades it sells (SA: 91/95/diesel). A 98 request in SA falls back to 95 with a clear note (fix C7).

### FX

- SAR, AED, QAR, BHD, OMR and JOD are **pegged to USD**, so their rates are effectively constants.
- Only KWD (a basket peg) and the non-GCC currencies actually move.
- Use a free daily FX feed for USD/KWD and EUR etc., cache it daily, and keep the pegs as constants that act as a sanity check.

## 6. Suggested order

| Step | Content | Size |
|---|---|---|
| 1 | Fixes C2–C8: real country polygons (Natural Earth admin-0 GeoJSON loaded in memory, point-in-polygon), honest totals, currency default, return duration, grade availability, reserve | S–M |
| 2 | Move tests to xUnit; add API integration tests | S |
| 3 | Price table + date-aware provider + staleness/confidence + FX pegs | M |
| 4 | Station extract (OSM) + `IRefuelPlanner` + stop list UI (fixes C1) | M–L |
| 5 | Border crossings JSON + detection + delays/tolls/insurance + arrival time | M |
| 6 | GlobalPetrolPrices feed, Places search-along-route probe, PostGIS (optional) | M |

Steps 1–3 can land without any paid API. Step 4 is the feature that changes the answer users get.

## Sources

- GlobalPetrolPrices data access: https://www.globalpetrolprices.com/data_access.php
- Google Places API `fuelOptions` (Place resource): https://developers.google.com/maps/documentation/places/web-service/reference/rest/v1/places
- GCC monthly price round-up (Nov 2026): https://www.yallamotor.com/news/gcc-fuel-prices-november-2026-55905
- Kuwait Q1 2026 prices: https://gulfnews.com/amp/story/business%2Fenergy%2Fkuwait-sets-fuel-prices-for-first-quarter-of-2026-1.500392620
- King Fahd Causeway live times / JESR: https://blog.wego.com/king-fahd-causeway-status/
- Saudi land crossings and peak waits: https://houseofsaud.com/travel/saudi-border-crossing/
- Kuwait–Saudi by road (Nuwaiseeb): https://blog.wego.com/travel-from-kuwait-to-saudi-arabia-by-road/
