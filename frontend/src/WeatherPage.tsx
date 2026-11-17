import { useCallback, useEffect, useMemo, useState } from 'react'

type DistrictWeather = {
  district: string
  latitude: number
  longitude: number
  observationTime: string
  temperatureC: number
  humidityPercent: number
  precipitationMm: number
  weatherCode: number
  retrievedAtUtc: string
  source: string
  sourceUrl: string
}

function weatherDescription(code: number) {
  if (code === 0) return 'Clear sky'
  if (code === 1) return 'Mainly clear'
  if (code === 2) return 'Partly cloudy'
  if (code === 3) return 'Overcast'
  if (code === 45 || code === 48) return 'Fog'
  if ([51, 53, 55, 56, 57].includes(code)) return 'Drizzle'
  if ([61, 63, 65, 66, 67].includes(code)) return 'Rain'
  if ([71, 73, 75, 77].includes(code)) return 'Snow'
  if ([80, 81, 82].includes(code)) return 'Rain showers'
  if ([85, 86].includes(code)) return 'Snow showers'
  if ([95, 96, 99].includes(code)) return 'Thunderstorm'
  return 'Conditions unavailable'
}

function formatUpdatedAt(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export default function WeatherPage() {
  const [districts, setDistricts] = useState<DistrictWeather[]>([])
  const [error, setError] = useState('')
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [retrievedAt, setRetrievedAt] = useState('')

  const refresh = useCallback(async (background = false) => {
    if (!background) setRefreshing(true)
    setError('')
    try {
      const response = await fetch('/api/weather-observations', { credentials: 'include' })
      if (!response.ok) {
        throw new Error(`District weather could not be refreshed (${response.status}).`)
      }
      const latest = await response.json() as DistrictWeather[]
      if (latest.length !== 25) {
        throw new Error(`Expected weather for 25 districts, but received ${latest.length}.`)
      }
      setDistricts(latest)
      setRetrievedAt(latest[0]?.retrievedAtUtc ?? '')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'District weather could not be refreshed.')
    } finally {
      setLoading(false)
      if (!background) setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    const initialRequest = window.setTimeout(() => void refresh(), 0)
    const timer = window.setInterval(() => void refresh(true), 15 * 60 * 1000)
    return () => {
      window.clearTimeout(initialRequest)
      window.clearInterval(timer)
    }
  }, [refresh])

  const visibleDistricts = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    return normalizedQuery
      ? districts.filter((item) => item.district.toLowerCase().includes(normalizedQuery))
      : districts
  }, [districts, query])

  return (
    <div className="extension-page live-data-page">
      <section className="extension-card">
        <div className="live-data-toolbar">
          <div className="extension-toolbar-copy">
            <span className="panel-eyebrow">CURRENT DISTRICT CONDITIONS</span>
            <h2>Weather across Sri Lanka</h2>
            <p>Current model conditions for all 25 district centers. The API refreshes every 15 minutes.</p>
          </div>
          <button type="button" className="extension-secondary-button" onClick={() => void refresh()} disabled={refreshing}>
            {refreshing ? 'Updating…' : 'Update now'}
          </button>
        </div>

        <div className="live-data-meta">
          <span>{retrievedAt ? `Last retrieved ${formatUpdatedAt(retrievedAt)}` : 'Waiting for current conditions'}</span>
          <label className="live-data-search">
            <span className="visually-hidden">Filter districts</span>
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Filter districts…" />
          </label>
        </div>

        {error && (
          <div className="extension-alert extension-alert-error" role="alert">
            {error}{districts.length > 0 && ' Showing the last successful readings.'}
          </div>
        )}
        {loading && districts.length === 0 && <p className="live-data-empty" role="status">Loading current district weather…</p>}
        {!loading && districts.length === 0 && !error && <p className="live-data-empty">No current district weather is available.</p>}

        {visibleDistricts.length > 0 && (
          <div className="live-weather-grid">
            {visibleDistricts.map((district) => (
              <article className="live-weather-card" key={district.district}>
                <div className="live-weather-card-heading">
                  <strong>{district.district}</strong>
                  <span>{district.observationTime.replace('T', ' ')} SLST</span>
                </div>
                <div className="live-weather-main">
                  <strong>{district.temperatureC.toFixed(1)}°C</strong>
                  <span>{weatherDescription(district.weatherCode)}</span>
                </div>
                <div className="live-weather-facts">
                  <span>Humidity <strong>{Math.round(district.humidityPercent)}%</strong></span>
                  <span>Precipitation <strong>{district.precipitationMm.toFixed(1)} mm</strong></span>
                </div>
              </article>
            ))}
          </div>
        )}

        {!loading && districts.length > 0 && visibleDistricts.length === 0 && (
          <p className="live-data-empty">No districts match “{query}”.</p>
        )}

        <p className="live-data-attribution">
          Current conditions are model-based estimates at district-center coordinates, not readings from a weather station.
          <a href="https://open-meteo.com/en/docs" target="_blank" rel="noreferrer"> {districts[0]?.source || 'Weather data by Open-Meteo.com'}</a>
          {' · '}<a href="https://creativecommons.org/licenses/by/4.0/" target="_blank" rel="noreferrer">CC BY 4.0</a>
        </p>
      </section>
    </div>
  )
}
