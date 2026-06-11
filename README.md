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
