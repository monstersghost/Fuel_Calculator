
# Cross-Border Trip Fuel Cost Calculator

A small app for estimating fuel usage and trip cost across borders.

The app calculates route distance, expected fuel consumption, and estimated fuel cost based on vehicle consumption, selected fuel type, and country-specific fuel prices.

## Features

- Calculate trip fuel usage from route distance
- Support cross-border route cost breakdown
- Select fuel type:
  - 91 Octane
  - 95 Octane
  - 98 Octane
  - Diesel
- Enter vehicle fuel consumption manually
- Support multiple consumption units:
  - L/100 km
  - km/L
  - US MPG
  - UK MPG
- Estimate cost per country
- Support manual fuel price overrides
- Convert total trip cost to a selected currency
- Optional tank size input for estimated refuel needs

## Goal

The goal is to make road trip cost planning easier, especially for long routes that pass through multiple countries where fuel prices and fuel grades differ.

Example use cases:

- Kuwait to Saudi Arabia road trips
- Kuwait to UAE trips
- GCC cross-border travel
- Comparing 91 vs 95 vs 98 fuel costs
- Estimating diesel vs gasoline trip cost
- Planning fuel budget before travel

## How It Works

The app uses the following basic formula:

```text
fuel needed = distance km × fuel consumption L/100km ÷ 100
cost = fuel needed × price per liter
````

For cross-border trips, the route is split by country:

```text
country distance × vehicle consumption × local fuel price
```

Then all country costs are combined into a final trip estimate.

## Tech Stack

### Backend

* .NET 8
* ASP.NET Core Web API
* Provider-based architecture for:

  * Route calculation
  * Fuel prices
  * Currency conversion
  * Vehicle data

### Frontend

* React
* Vite

### Database

* PostgreSQL
* PostGIS planned for accurate route/country boundary calculations
* In-memory/mock mode for the current MVP

## Run

Start the API:

```powershell
dotnet restore FuelCalculator.sln --configfile NuGet.Config
dotnet build FuelCalculator.sln
dotnet run --project src\FuelCalculator.Api\FuelCalculator.Api.csproj --urls http://localhost:5124
```

Start the frontend in another terminal:

```powershell
cd frontend
npm.cmd install
npm.cmd run dev
```

Open `http://127.0.0.1:5173`. Vite proxies `/api` to `http://localhost:5124`.

## Test

```powershell
dotnet run --project tests\FuelCalculator.Tests\FuelCalculator.Tests.csproj
cd frontend
npm.cmd run build
```

## API

Main endpoint:

```http
POST /api/trips/estimate
```

Example response:

```json
{
  "totalDistanceKm": 1270.5,
  "normalizedConsumptionLPer100Km": 8.5,
  "totalFuelLiters": 108.0,
  "outputCurrency": "KWD",
  "totalCost": 32.4,
  "segments": [
    {
      "countryCode": "KW",
      "distanceKm": 120.0,
      "fuelType": "GASOLINE_95",
      "fuelLiters": 10.2,
      "pricePerLiter": 0.105,
      "priceCurrency": "KWD",
      "localCost": 1.071,
      "convertedCost": 1.071,
      "priceSource": "Manual"
    }
  ],
  "warnings": []
}
```

## Fuel Consumption Conversion

Internally, all fuel consumption values are normalized to `L/100 km`.

```text
L/100km = input value
km/L    = 100 / input value
US MPG  = 235.214583 / input value
UK MPG  = 282.480936 / input value
```

## Fuel Price Sources

The app is designed to support multiple fuel price providers.

Initial MVP providers:

* Manual fuel price input
* Static seed fuel prices for development

Future providers:

* Official country fuel price sources
* Global fuel price APIs
* Country-specific GCC fuel price adapters

## Implemented MVP Scope

The current version includes:

* Manual origin/destination input
* Optional waypoint and Google Maps link fields
* Manual vehicle consumption input
* Fuel type selection
* Manual/static fuel prices
* Trip fuel cost calculation
* Per-country cost breakdown
* Clean backend interfaces for future live data integrations
* Mock route provider for local development without API keys
* Google Routes provider interface/adapter
* Approximate polyline country segmentation
* Static currency conversion
* Basic in-memory vehicle profile storage
* Optional tank size and current fuel input for refuel-stop estimation

## Future Improvements

* Google Routes API integration
* Google Maps link import
* Automatic country segmentation
* Live fuel price provider integration
* Currency exchange API integration
* Saved vehicle profiles
* Saved trips
* Refuel stop estimation
* Station-level fuel planning
* EV charging support

## Status

MVP implemented with a provider-based backend, React UI, mock/local mode, and unit/integration-style tests.

