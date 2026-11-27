# Fuel price seed table

The seed table (`StaticSeedFuelPriceProvider`) is the last fallback, used only when there is no manual price and no
live or official feed for a country. Every price was checked on **2026-11-27**. Each row in the code carries its source,
the date it took effect and a confidence level, and the API returns all three with the quote.

**Update this file and the table together.** Most GCC prices change on the 1st of the month:
- UAE and Qatar announce on the last day of the month.
- Kuwait sets prices quarterly.
- Oman and Bahrain set them monthly.

Prices rose sharply in 2026. UAE petrol was about 80% above February levels in November, after the Iran war and the
closure of the Strait of Hormuz.

## Prices per litre

| Country | 91 | 95 | 98 | Diesel | Currency | In effect from | Confidence | Source |
|---|---|---|---|---|---|---|---|---|
| Kuwait | 0.085 | 0.105 | 0.200 | 0.115 | KWD | 2026-01-01 | MEDIUM (98: LOW) | Subsidies committee, Q1 2026; reported unchanged since. 98 is set quarterly and later figures conflict (200 vs 275 fils). |
| Saudi Arabia | 2.18 | 2.33 | 5.09 | 1.79 | SAR | 2026-11-01 | HIGH (98: MEDIUM) | Aramco retail fuels page, November 2026. One October report gave 98 at 5.33. |
| UAE | 3.97 | 4.04 | 4.16 | 4.75 | AED | 2026-11-01 | HIGH | UAE Fuel Price Committee (WAM), November 2026. |
| Qatar | 1.95 | 2.10 | – | 2.05 | QAR | 2026-11-01 | MEDIUM | QatarEnergy: November unchanged from October (QNA); figures from YallaMotor. No 98 sold. |
| Bahrain | 0.235 | 0.277 | 0.410 | 0.260 | BHD | 2026-11-01 | MEDIUM | Fuel Price Committee, November 2026 (News of Bahrain). |
| Oman | 0.229 | 0.239 | 0.399 | 0.258 | OMR | 2026-11-01 | MEDIUM | Ministry of Energy and Minerals, November 2026 (Khaleej Times, YallaMotor). |
| Jordan | 1.050 (Octane 90) | 1.360 | – | 0.900 | JOD | 2026-10-01 | LOW | Fuel pricing committee, October 2026 (Okaz citing Petra). November not found. |
| Syria | 185 (Octane 90) | 195 | – | 175 | SYP (new) | 2026-09-13 | LOW | Ministry of Energy hike of 13 September 2026 (Enab Baladi, Al Jazeera). Diesel has tiers of 115/150/175; 175 is standard. |
| Iraq | – | 850 | – | – | IQD | 2026-08-10 | LOW | GlobalPetrolPrices, federal price. Kurdistan Region stations charge 2–3× more. Diesel sources disagree (250–1,000), so it isn't seeded. |
| Lebanon | – | 122,750 | 123,700 | 118,750 | LBP | 2026-08-11/18 | LOW | LBCI, per-20-litre ministry prices ÷ 20. Prices fell in October and November, but no figures were found. |
| Egypt | 22.25 (Octane 92) | 24.00 | – | 20.50 | EGP | 2026-03-10 | MEDIUM | Frozen until the end of December 2026 (Ahram Online, Rio Times). |

"–" means not seeded. When a grade has no price, the calculator uses the nearest priced grade and adds a warning:
- If the country doesn't sell the grade: "98 Octane is not sold in QA; priced as 95 Octane".
- If it sells the grade but no price was found: "No 91 Octane price for IQ; used the 95 Octane price as an estimate".

## Exchange rates (`StaticCurrencyConverter`)

- **Fixed to the US dollar, so these are exact:** SAR 3.75, AED 3.6725, QAR 3.64, BHD 0.376, OMR 0.3845 and JOD 0.709 per USD.
- **Estimated, checked 2026-11-27:**
  - KWD (basket peg): 0.3075 per USD.
  - EGP: about 53.7 per USD (bank quotes, 15 Nov 2026).
  - IQD: 1,310 per USD (official).
  - LBP: about 89,600 per USD.
  - SYP: about 115 per USD in new pounds, from the old-pound market rate of about 11,000–12,000. Syria dropped two zeros on 1 January 2026.

## Rules that affect refuelling

- **Oman:** a temporary limit of 30 litres per fill-up at stations within 50 km of a border crossing, in force since late
  October 2026. It is unclear whether this is per person or per vehicle, and how long it will last. You may refuel more than once a day.

## Sources

- UAE: https://www.wam.ae/a/17i2n96 · https://www.thenationalnews.com/business/energy/2026/10/31/uae-petrol-prices-defy-expectations-with-surprise-drop-in-november/
- Saudi Arabia: https://www.aramco.com/en/what-we-do/energy-products/retail-fuels
- Qatar: https://qna.org.qa/en/News-Area/News/2026-10/31/qatarenergy-sets-fuel-prices-for-november-2026 · https://www.yallamotor.com/news/fuel-prices-qatar-november-2026-en-55833
- Oman: https://www.yallamotor.com/news/fuel-prices-oman-november-2026-en-55835 · https://www.khaleejtimes.com/gulf/oman-temporary-cap-fuel-refill-border-crossing-stations
- Kuwait: https://gulfnews.com/business/energy/kuwait-sets-fuel-prices-for-first-quarter-of-2026-1.500392620 · https://www.yallamotor.com/news/fuel-prices-kuwait-november-2026-en-55837
- Bahrain: https://www.newsofbahrain.com/en/article/bahrain-announces-fuel-prices-for-november
- GCC roundup: https://www.yallamotor.com/news/gcc-fuel-prices-november-2026-55905
- Jordan: https://www.okaz.com.sa/economy/na/2268793 · https://jppmc.jo/en/Fuel_Prices
- Syria: https://english.enabbaladi.net/archives/2026/09/syria-raises-fuel-prices-in-unprecedented-hike/amp/ · https://www.aljazeera.com/news/2026/9/16/people-dont-have-money-syrian-fuel-hikes-deepen-economic-pain
- Iraq: https://www.globalpetrolprices.com/Iraq/gasoline_prices/
- Lebanon: https://www.lbcgroup.tv/news/economy/891843/lebanons-fuel-prices-slightly-increase/en
- Egypt: https://english.ahram.org.eg/News/580080.aspx · https://www.riotimesonline.com/egypt-petrol-diesel-prices-unchanged-december-2026/
- Syrian pound: https://www.aljazeera.com/economy/2026/1/5/syrias-new-currency-removes-al-assad-family-images-seeks-to-boost-economy
- Egyptian pound: https://www.arabfinance.com/en/news/newdetails/usd-egp-exchange-rate-on-november-15-2026
