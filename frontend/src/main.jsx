import React from 'react';
import { createRoot } from 'react-dom/client';
import { Calculator, Circle, CircleDollarSign, Fuel, GripVertical, MapPin, MapPinned, Plus, Route, Search, Trash2 } from 'lucide-react';
import './styles.css';

const fuelTypes = [
  { value: 'GASOLINE_91', label: '91 Octane' },
  { value: 'GASOLINE_95', label: '95 Octane' },
  { value: 'GASOLINE_98', label: '98 Octane' },
  { value: 'DIESEL', label: 'Diesel' },
];

const consumptionUnits = [
  { value: 'L_PER_100KM', label: 'L/100km' },
  { value: 'KM_PER_L', label: 'km/L' },
  { value: 'US_MPG', label: 'US MPG' },
  { value: 'UK_MPG', label: 'UK MPG' },
];

// Shown first in the currency lists; the rest come from the API's exchange-rate snapshot.
const commonCurrencies = ['KWD', 'SAR', 'AED', 'QAR', 'BHD', 'OMR', 'USD', 'EUR', 'GBP', 'JOD'];

function useCurrencies() {
  const [data, setData] = React.useState({ currencies: commonCurrencies, asOf: null });

  React.useEffect(() => {
    let cancelled = false;
    fetch('/api/currencies')
      .then((response) => (response.ok ? response.json() : null))
      .then((body) => {
        if (!cancelled && body?.currencies?.length) {
          const rest = body.currencies.filter((code) => !commonCurrencies.includes(code));
          setData({ currencies: [...commonCurrencies, ...rest], asOf: body.asOf });
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  return data;
}

const initialForm = {
  googleMapsLink: '',
  fuelType: 'GASOLINE_95',
  consumptionValue: '8.5',
  consumptionUnit: 'L_PER_100KM',
  outputCurrency: 'KWD',
  tankSizeLiters: '70',
  currentFuelPercentage: '100',
  reservePercentage: '15',
};

const initialStops = [
  { label: 'Home', coordinates: '' },
  { label: 'Hafar Al Batin Saudi Arabia', coordinates: '' },
  { label: 'Al Hadithah Saudi Arabia', coordinates: '' },
  { label: 'Al Nabk, Syria', coordinates: '' },
];

function App() {
  const [form, setForm] = React.useState(initialForm);
  const [stops, setStops] = React.useState(initialStops);
  const [manualPrices, setManualPrices] = React.useState([
    createManualPrice({ countryCode: 'KW', fuelType: 'GASOLINE_95', pricePerLiter: '' }),
  ]);
  const [result, setResult] = React.useState(null);
  const [error, setError] = React.useState('');
  const [loading, setLoading] = React.useState(false);
  const { currencies, asOf: ratesAsOf } = useCurrencies();

  function updateField(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  function updateManualPrice(index, field, value) {
    setManualPrices((current) =>
      current.map((price, priceIndex) => (priceIndex === index ? { ...price, [field]: value } : price)),
    );
  }

  function addManualPrice() {
    setManualPrices((current) => [
      ...current,
      createManualPrice({ countryCode: '', fuelType: form.fuelType, pricePerLiter: '' }),
    ]);
  }

  function removeManualPrice(index) {
    setManualPrices((current) => current.filter((_, priceIndex) => priceIndex !== index));
  }

  function updateStop(index, field, value) {
    setStops((current) =>
      current.map((stop, stopIndex) => (stopIndex === index ? { ...stop, [field]: value } : stop)),
    );
  }

  function addStop() {
    setStops((current) => [...current, { label: '', coordinates: '' }]);
  }

  function removeStop(index) {
    setStops((current) => current.filter((_, stopIndex) => stopIndex !== index));
  }

  async function submitEstimate(event) {
    event.preventDefault();
    setLoading(true);
    setError('');
    setResult(null);

    const routeStops = stops
      .map((stop) => formatRouteStop(stop))
      .filter(Boolean);
    const hasGoogleMapsLink = form.googleMapsLink.trim().length > 0;

    if (!hasGoogleMapsLink && routeStops.length < 2) {
      setError('At least two route stops are required.');
      setLoading(false);
      return;
    }

    const payload = {
      origin: hasGoogleMapsLink ? null : routeStops[0],
      destination: hasGoogleMapsLink ? null : routeStops[routeStops.length - 1],
      googleMapsLink: form.googleMapsLink || null,
      waypoints: hasGoogleMapsLink ? [] : routeStops.slice(1, -1),
      fuelType: form.fuelType,
      consumptionValue: Number(form.consumptionValue),
      consumptionUnit: form.consumptionUnit,
      outputCurrency: form.outputCurrency,
      tankSizeLiters: form.tankSizeLiters ? Number(form.tankSizeLiters) : null,
      currentFuelPercentage: form.currentFuelPercentage ? Number(form.currentFuelPercentage) : null,
      reservePercentage: form.reservePercentage === '' ? null : Number(form.reservePercentage),
      manualFuelPrices: manualPrices
        .filter((price) => price.countryCode && price.pricePerLiter)
        .map((price) => ({
          countryCode: price.countryCode,
          fuelType: price.fuelType || form.fuelType,
          pricePerLiter: Number(price.pricePerLiter),
          // Blank means the country's own currency; the API fills it in.
          currency: price.currency || null,
        })),
    };

    try {
      const response = await fetch('/api/trips/estimate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      const data = await response.json();

      if (!response.ok) {
        const messages = Array.isArray(data.errors) ? data.errors.join(' ') : data.detail || 'Estimate failed.';
        setError(messages);
        setResult(data.warnings?.length ? { warnings: data.warnings, segments: [] } : null);
        return;
      }

      setResult({ ...data, requestedFuelType: payload.fuelType });
    } catch (estimateError) {
      setError(estimateError instanceof Error ? estimateError.message : 'Estimate failed.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">Cross-border fuel planning</p>
          <h1>Fuel Cost Estimator</h1>
        </div>
        <div className="status-pill">Mock route mode</div>
      </header>

      <section className="workspace">
        <form className="panel form-panel" onSubmit={submitEstimate}>
          <SectionTitle icon={<Route size={18} />} title="Route" />
          <div className="route-builder">
            {stops.map((stop, index) => (
              <StopRow
                key={index}
                index={index}
                stopCount={stops.length}
                stop={stop}
                canRemove={stops.length > 2}
                onChange={updateStop}
                onRemove={removeStop}
              />
            ))}
            <button className="add-stop-button" type="button" onClick={addStop}>
              <Plus size={18} />
              Add destination
            </button>
          </div>
          <TextField
            label="Google Maps link"
            value={form.googleMapsLink}
            onChange={(value) => updateField('googleMapsLink', value)}
            placeholder="Optional"
          />

          <SectionTitle icon={<Fuel size={18} />} title="Vehicle" />
          <div className="field-grid three">
            <label className="field">
              <span>Fuel type</span>
              <select value={form.fuelType} onChange={(event) => updateField('fuelType', event.target.value)}>
                {fuelTypes.map((fuelType) => (
                  <option key={fuelType.value} value={fuelType.value}>
                    {fuelType.label}
                  </option>
                ))}
              </select>
            </label>
            <TextField
              label="Consumption"
              type="number"
              min="0.01"
              step="0.01"
              value={form.consumptionValue}
              onChange={(value) => updateField('consumptionValue', value)}
            />
            <label className="field">
              <span>Unit</span>
              <select value={form.consumptionUnit} onChange={(event) => updateField('consumptionUnit', event.target.value)}>
                {consumptionUnits.map((unit) => (
                  <option key={unit.value} value={unit.value}>
                    {unit.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="field-grid">
            <label className="field">
              <span>Output currency</span>
              <select value={form.outputCurrency} onChange={(event) => updateField('outputCurrency', event.target.value)}>
                {currencies.map((currency) => (
                  <option key={currency} value={currency}>
                    {currency}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="field-grid three">
            <TextField
              label="Tank size"
              type="number"
              min="0"
              step="0.1"
              value={form.tankSizeLiters}
              onChange={(value) => updateField('tankSizeLiters', value)}
              placeholder="Liters"
            />
            <TextField
              label="Current fuel"
              type="number"
              min="0"
              max="100"
              step="1"
              value={form.currentFuelPercentage}
              onChange={(value) => updateField('currentFuelPercentage', value)}
              placeholder="%"
            />
            <TextField
              label="Keep in reserve"
              type="number"
              min="0"
              max="99"
              step="1"
              value={form.reservePercentage}
              onChange={(value) => updateField('reservePercentage', value)}
              placeholder="%"
            />
          </div>

          <div className="manual-header">
            <SectionTitle icon={<CircleDollarSign size={18} />} title="Manual prices" />
            <button className="icon-button" type="button" onClick={addManualPrice} aria-label="Add manual price">
              <Plus size={18} />
            </button>
          </div>

          <div className="manual-list">
            {manualPrices.map((price, index) => (
              <div className="manual-row" key={price.id}>
                <TextField
                  label="Country"
                  value={price.countryCode}
                  onChange={(value) => updateManualPrice(index, 'countryCode', value.toUpperCase())}
                  placeholder="KW"
                />
                <label className="field">
                  <span>Fuel</span>
                  <select value={price.fuelType} onChange={(event) => updateManualPrice(index, 'fuelType', event.target.value)}>
                    {fuelTypes.map((fuelType) => (
                      <option key={fuelType.value} value={fuelType.value}>
                        {fuelType.label}
                      </option>
                    ))}
                  </select>
                </label>
                <TextField
                  label="Price"
                  type="number"
                  min="0"
                  step="0.001"
                  value={price.pricePerLiter}
                  onChange={(value) => updateManualPrice(index, 'pricePerLiter', value)}
                />
                <label className="field">
                  <span>Currency</span>
                  <select value={price.currency} onChange={(event) => updateManualPrice(index, 'currency', event.target.value)}>
                    <option value="">Local</option>
                    {currencies.map((currency) => (
                      <option key={currency} value={currency}>
                        {currency}
                      </option>
                    ))}
                  </select>
                </label>
                <button className="icon-button remove" type="button" onClick={() => removeManualPrice(index)} aria-label="Remove manual price">
                  <Trash2 size={17} />
                </button>
              </div>
            ))}
          </div>

          <button className="primary-button" type="submit" disabled={loading}>
            <Calculator size={19} />
            {loading ? 'Calculating' : 'Calculate'}
          </button>
          <p className="attribution">
            <a href="https://www.exchangerate-api.com" target="_blank" rel="noreferrer">
              Rates By Exchange Rate API
            </a>
            {ratesAsOf ? ` · ${ratesAsOf}` : ''}
          </p>
        </form>

        <section className="results-space">
          {error ? <div className="notice error">{error}</div> : null}
          {result ? <Results result={result} /> : <EmptyResults />}
        </section>
      </section>
    </main>
  );
}

function StopRow({ index, stopCount, stop, canRemove, onChange, onRemove }) {
  const isLast = index === stopCount - 1;

  return (
    <div className="stop-row">
      <div className="stop-rail" aria-hidden="true">
        {index === 0 ? <Circle size={15} /> : isLast ? <MapPin size={17} /> : <Circle size={15} />}
        <GripVertical size={16} />
      </div>
      <div className="stop-fields">
        <input
          value={stop.label}
          onChange={(event) => onChange(index, 'label', event.target.value)}
          placeholder={index === 0 ? 'Origin' : 'Destination or waypoint'}
          aria-label={`Route stop ${index + 1}`}
        />
        <input
          value={stop.coordinates}
          onChange={(event) => onChange(index, 'coordinates', event.target.value)}
          placeholder="GPS: 29.3759,47.9774"
          aria-label={`Route stop ${index + 1} GPS coordinates`}
        />
      </div>
      <Search className="stop-search" size={20} aria-hidden="true" />
      {canRemove ? (
        <button className="icon-button remove stop-remove" type="button" onClick={() => onRemove(index)} aria-label="Remove stop">
          <Trash2 size={17} />
        </button>
      ) : null}
    </div>
  );
}

function SectionTitle({ icon, title }) {
  return (
    <div className="section-title">
      {icon}
      <h2>{title}</h2>
    </div>
  );
}

function TextField({ label, value, onChange, type = 'text', ...props }) {
  return (
    <label className="field">
      <span>{label}</span>
      <input type={type} value={value} onChange={(event) => onChange(event.target.value)} {...props} />
    </label>
  );
}

function EmptyResults() {
  return (
    <div className="empty-state">
      <MapPinned size={30} />
      <span>Run an estimate</span>
    </div>
  );
}

function Results({ result }) {
  const fuelType = result.requestedFuelType ?? result.segments?.[0]?.fuelType ?? 'GASOLINE_95';
  const missing = result.countriesMissingPrice ?? [];
  const totalIncomplete = result.isTotalComplete === false;
  const plan = result.refuelPlan;

  return (
    <div className="results">
      <div className="metric-grid">
        <Metric label="Distance" value={`${formatNumber(result.totalDistanceKm)} km`} />
        <Metric label="Driving time" value={formatDuration(result.durationMinutes)} />
        <Metric label="Fuel needed" value={`${formatNumber(result.totalFuelLiters)} L`} />
        {plan ? (
          <Metric
            label={plan.isComplete ? 'Planned fuel cost' : 'Planned fuel cost (incomplete)'}
            value={`${formatMoney(plan.tripFuelCost)} ${result.outputCurrency ?? ''}`}
            note={result.refuelSaving > 0 ? `Saves ${formatMoney(result.refuelSaving)} ${result.outputCurrency} by buying where it is cheaper` : null}
            positive
          />
        ) : null}
        <Metric
          label={`${plan ? 'At local prices' : 'Total cost'}${totalIncomplete ? ' (incomplete)' : ''}`}
          value={`${formatMoney(result.totalCost)} ${result.outputCurrency ?? ''}`}
          note={totalIncomplete ? `Excludes ${missing.join(', ')}: no price` : null}
        />
        <Metric label="Fuel type" value={fuelLabel(fuelType)} />
      </div>

      {plan ? <RefuelPlanView plan={plan} currency={result.outputCurrency} /> : null}

      {result.fuelStops ? (
        <div className="fuel-stop-strip">
          <span>{formatNumber(result.fuelStops.fullTankRangeKm)} km full-tank range</span>
          <span>
            {formatNumber(result.fuelStops.usableRangeKm)} km per tank keeping {formatNumber(result.fuelStops.reservePercentage)}% in reserve
          </span>
          {plan ? null : (
            <span>{result.fuelStops.estimatedMinimumStops} minimum refuel stop{result.fuelStops.estimatedMinimumStops === 1 ? '' : 's'}</span>
          )}
        </div>
      ) : null}

      {result.warnings?.length ? (
        <div className="notice warning">
          {result.warnings.map((warning) => (
            <p key={warning}>{warning}</p>
          ))}
        </div>
      ) : null}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Country</th>
              <th>Distance</th>
              <th>Liters</th>
              <th>Price</th>
              <th>Local cost</th>
              <th>Converted ({result.outputCurrency})</th>
              <th>Source</th>
            </tr>
          </thead>
          <tbody>
            {result.segments?.map((segment) => (
              <tr key={segment.countryCode}>
                <td>{segment.countryCode}</td>
                <td>{formatNumber(segment.distanceKm)} km</td>
                <td>{formatNumber(segment.fuelLiters)} L</td>
                <td>
                  {segment.pricePerLiter == null
                    ? 'Missing'
                    : `${formatMoney(segment.pricePerLiter)} ${segment.priceCurrency}/L`}
                  {segment.fuelType !== fuelType ? <span className="grade-note">as {fuelLabel(segment.fuelType)}</span> : null}
                </td>
                <td>{segment.localCost == null ? '-' : `${formatMoney(segment.localCost)} ${segment.priceCurrency}`}</td>
                <td className="converted-cell">
                  {segment.convertedCost == null ? '-' : `${formatMoney(segment.convertedCost)} ${result.outputCurrency}`}
                </td>
                <td>
                  <span className={segment.isUserProvided ? 'source-badge manual' : 'source-badge'}>
                    {segment.priceSource ?? 'Missing'}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Metric({ label, value, note, positive = false }) {
  return (
    <div className="metric-card">
      <span>{label}</span>
      <strong>{value}</strong>
      {note ? <small className={positive ? 'metric-note positive' : 'metric-note'}>{note}</small> : null}
    </div>
  );
}

function RefuelPlanView({ plan, currency }) {
  return (
    <section className="refuel-plan" aria-label="Refuel plan">
      <div className="refuel-head">
        <h2>
          <Fuel size={18} /> Refuel plan
        </h2>
        <span>
          Start with {formatNumber(plan.startFuelLiters)} L · buy {formatNumber(plan.purchasedLiters)} L · arrive with{' '}
          {formatNumber(plan.arrivalFuelLiters)} L ({formatNumber(plan.arrivalFuelPercentage)}%)
        </span>
      </div>

      {plan.stops.length === 0 ? (
        <p className="refuel-empty">No stop needed: the fuel in the tank reaches the destination.</p>
      ) : (
        <ol className="refuel-stops">
          {plan.stops.map((stop, index) => (
            <li key={`${stop.distanceKm}-${index}`}>
              <div className="refuel-where">
                <strong>km {formatNumber(stop.distanceKm)}</strong>
                <span className="country-chip">{stop.countryCode}</span>
                {stop.name ? <span className="station-name">{stop.name}</span> : null}
              </div>
              <div className="refuel-what">
                <strong>
                  {formatNumber(stop.liters)} L
                  {stop.pricePerLiter == null ? ' · price unknown' : ` × ${formatMoney(stop.pricePerLiter)} ${stop.currency}`}
                </strong>
                <span>{stop.reason}</span>
              </div>
              <div className="refuel-cost">
                <strong>{stop.convertedCost == null ? '-' : `${formatMoney(stop.convertedCost)} ${currency}`}</strong>
                <span>
                  {formatNumber(stop.fuelBeforePercentage)}% → {formatNumber(stop.fuelAfterPercentage)}%
                </span>
              </div>
            </li>
          ))}
        </ol>
      )}

      <p className="refuel-sum">
        Bought {formatMoney(plan.purchasedCost)} + fuel you start with {formatMoney(plan.startFuelValue)} − fuel left on arrival{' '}
        {formatMoney(plan.arrivalFuelValue)} = <strong>{formatMoney(plan.tripFuelCost)} {currency}</strong>
      </p>

      {plan.warnings?.some((warning) => warning.includes('OpenStreetMap fuel stations')) ? (
        <p className="attribution">
          Fuel stations ©{' '}
          <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">
            OpenStreetMap contributors
          </a>
        </p>
      ) : null}

      {plan.warnings?.length ? (
        <div className={plan.isFeasible ? 'notice subtle' : 'notice error'}>
          {plan.warnings.map((warning) => (
            <p key={warning}>{warning}</p>
          ))}
        </div>
      ) : null}
    </section>
  );
}

function formatDuration(minutes) {
  if (minutes == null || Number.isNaN(Number(minutes))) {
    return '-';
  }

  const total = Math.round(Number(minutes));
  const hours = Math.floor(total / 60);
  const rest = total % 60;
  return hours > 0 ? `${hours} h ${rest} min` : `${rest} min`;
}

function fuelLabel(value) {
  return fuelTypes.find((fuelType) => fuelType.value === value)?.label ?? value;
}

function formatNumber(value) {
  if (value == null || Number.isNaN(Number(value))) {
    return '-';
  }

  return Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function formatMoney(value) {
  if (value == null || Number.isNaN(Number(value))) {
    return '-';
  }

  return Number(value).toLocaleString(undefined, { minimumFractionDigits: 3, maximumFractionDigits: 3 });
}

function createManualPrice(overrides = {}) {
  const id = typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random()}`;

  return {
    id,
    countryCode: '',
    fuelType: 'GASOLINE_95',
    pricePerLiter: '',
    currency: '',
    ...overrides,
  };
}

function formatRouteStop(stop) {
  const label = stop.label.trim();
  const coordinates = stop.coordinates.trim();

  if (label && coordinates) {
    return `${label} | ${coordinates}`;
  }

  return label || coordinates;
}

createRoot(document.getElementById('root')).render(<App />);
