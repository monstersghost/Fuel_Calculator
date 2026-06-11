import React from 'react';
import { createRoot } from 'react-dom/client';
import { Calculator, CircleDollarSign, Fuel, MapPinned, Plus, Route, Trash2 } from 'lucide-react';
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

const currencies = ['KWD', 'SAR', 'AED', 'QAR', 'BHD', 'OMR', 'USD'];

const initialForm = {
  origin: 'Kuwait City',
  destination: 'Doha, Qatar',
  googleMapsLink: '',
  waypointsText: '',
  fuelType: 'GASOLINE_95',
  consumptionValue: '8.5',
  consumptionUnit: 'L_PER_100KM',
  outputCurrency: 'KWD',
  tankSizeLiters: '70',
  currentFuelPercentage: '100',
};

function App() {
  const [form, setForm] = React.useState(initialForm);
  const [manualPrices, setManualPrices] = React.useState([
    { countryCode: 'KW', fuelType: 'GASOLINE_95', pricePerLiter: '', currency: 'KWD' },
  ]);
  const [result, setResult] = React.useState(null);
  const [error, setError] = React.useState('');
  const [loading, setLoading] = React.useState(false);

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
      { countryCode: '', fuelType: form.fuelType, pricePerLiter: '', currency: form.outputCurrency },
    ]);
  }

  function removeManualPrice(index) {
    setManualPrices((current) => current.filter((_, priceIndex) => priceIndex !== index));
  }

  async function submitEstimate(event) {
    event.preventDefault();
    setLoading(true);
    setError('');
    setResult(null);

    const payload = {
      origin: form.origin,
      destination: form.destination,
      googleMapsLink: form.googleMapsLink || null,
      waypoints: form.waypointsText
        .split('\n')
        .map((waypoint) => waypoint.trim())
        .filter(Boolean),
      fuelType: form.fuelType,
      consumptionValue: Number(form.consumptionValue),
      consumptionUnit: form.consumptionUnit,
      outputCurrency: form.outputCurrency,
      tankSizeLiters: form.tankSizeLiters ? Number(form.tankSizeLiters) : null,
      currentFuelPercentage: form.currentFuelPercentage ? Number(form.currentFuelPercentage) : null,
      manualFuelPrices: manualPrices
        .filter((price) => price.countryCode && price.pricePerLiter)
        .map((price) => ({
          countryCode: price.countryCode,
          fuelType: price.fuelType || form.fuelType,
          pricePerLiter: Number(price.pricePerLiter),
          currency: price.currency || form.outputCurrency,
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

      setResult(data);
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
          <div className="field-grid">
            <TextField label="Origin" value={form.origin} onChange={(value) => updateField('origin', value)} />
            <TextField label="Destination" value={form.destination} onChange={(value) => updateField('destination', value)} />
          </div>
          <label className="field">
            <span>Waypoints</span>
            <textarea
              value={form.waypointsText}
              onChange={(event) => updateField('waypointsText', event.target.value)}
              rows={3}
              placeholder="One waypoint per line"
            />
          </label>
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

          <div className="field-grid three">
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
          </div>

          <div className="manual-header">
            <SectionTitle icon={<CircleDollarSign size={18} />} title="Manual prices" />
            <button className="icon-button" type="button" onClick={addManualPrice} aria-label="Add manual price">
              <Plus size={18} />
            </button>
          </div>

          <div className="manual-list">
            {manualPrices.map((price, index) => (
              <div className="manual-row" key={`${index}-${price.countryCode}`}>
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
        </form>

        <section className="results-space">
          {error ? <div className="notice error">{error}</div> : null}
          {result ? <Results result={result} /> : <EmptyResults />}
        </section>
      </section>
    </main>
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
  const fuelType = result.segments?.[0]?.fuelType ?? 'GASOLINE_95';

  return (
    <div className="results">
      <div className="metric-grid">
        <Metric label="Distance" value={`${formatNumber(result.totalDistanceKm)} km`} />
        <Metric label="Fuel needed" value={`${formatNumber(result.totalFuelLiters)} L`} />
        <Metric label="Total cost" value={`${formatMoney(result.totalCost)} ${result.outputCurrency ?? ''}`} />
        <Metric label="Fuel type" value={fuelLabel(fuelType)} />
      </div>

      {result.fuelStops ? (
        <div className="fuel-stop-strip">
          <span>{formatNumber(result.fuelStops.fullTankRangeKm)} km full-tank range</span>
          <span>{result.fuelStops.estimatedMinimumStops} minimum refuel stop{result.fuelStops.estimatedMinimumStops === 1 ? '' : 's'}</span>
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
              <th>Converted</th>
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
                </td>
                <td>{segment.localCost == null ? '-' : `${formatMoney(segment.localCost)} ${segment.priceCurrency}`}</td>
                <td>{segment.convertedCost == null ? '-' : `${formatMoney(segment.convertedCost)} ${result.outputCurrency}`}</td>
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

function Metric({ label, value }) {
  return (
    <div className="metric-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
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

createRoot(document.getElementById('root')).render(<App />);
