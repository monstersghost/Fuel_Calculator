# Border data

`border-crossings.json` is edited by hand. It holds:

- `crossings`: named land crossings, each with the two country codes, an approximate position (to within a few km), opening hours, the peak calendar it follows (`gcc` or `europe`), typical waits in minutes (`normal` / `peak` / `holiday`), costs (`perPerson` true means each person in the car pays), notes and source links.
- `holidayPeaks`: date windows when waits jump (Eid, national days, New Year). Islamic holiday dates are expected dates and can move by a day.
- `entryRules` / `exitRules`: requirements and costs for entering or leaving a country by road, whichever crossing is used.
- `openBorderGroups`: groups whose internal borders have no routine checks (Schengen).
- `defaultDelay`: used for a border with no crossing on file.

The waits are estimates from news reports and are marked LOW confidence in the API. When a route crosses more than 60 km from the nearest crossing on file, the result says so. Add a crossing to fix that.

Checked 2026-11-27. Keep `checkedOn` up to date when you edit the file.
