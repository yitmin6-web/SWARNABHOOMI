import { useCallback, useEffect, useState } from 'react'

type MarketPriceObservationItem = {
  id: string
  market: string
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  grade: string
  priceType: string
  value: number
  currency: string
  unit: string
  observationDate: string
  publicationDate: string | null
  source: string | null
}

type LatestMarketPriceReport = {
  reportDate: string
  pdfUrl: string | null
  sourceUrl: string
  sourceName: string
  updateNote: string
  checkedAtUtc: string
  isCachedFallback?: boolean
  warning?: string | null
  observations?: MarketPriceObservationItem[]
}

function formatDate(value: string) {
  const date = new Date(`${value}T00:00:00`)
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat('en', { dateStyle: 'long' }).format(date)
}

function formatTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function formatPrice(value: number, currency: string, unit: string) {
  return `${currency} ${value.toFixed(2)} / ${unit}`
}

export default function MarketPricesPage() {
  const [report, setReport] = useState<LatestMarketPriceReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const refresh = useCallback(async (background = false, forceRefresh = false) => {
    if (!background) setRefreshing(true)
    setError('')
    try {
      const endpoint = forceRefresh ? '/api/market-prices?forceRefresh=true' : '/api/market-prices'
      const response = await fetch(endpoint, { credentials: 'include' })
      if (!response.ok) {
        throw new Error(`The CBSL price report could not be checked (${response.status}).`)
      }
      const latest = await response.json() as LatestMarketPriceReport
      if (latest.pdfUrl) {
        const pdfUrl = new URL(latest.pdfUrl)
        if (pdfUrl.protocol !== 'https:' || pdfUrl.hostname !== 'www.cbsl.gov.lk') {
          throw new Error('The CBSL report page returned an unexpected report link.')
        }
      } else if (!latest.observations || latest.observations.length === 0) {
        throw new Error('No CBSL daily price report or cached price observations are available.')
      }
      setReport(latest)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'The CBSL price report could not be checked.')
    } finally {
      setLoading(false)
      if (!background) setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    const initialRequest = window.setTimeout(() => void refresh(), 0)
    const timer = window.setInterval(() => void refresh(true), 60 * 60 * 1000)
    return () => {
      window.clearTimeout(initialRequest)
      window.clearInterval(timer)
    }
  }, [refresh])

  const safePdfUrl = report?.pdfUrl ? `${report.pdfUrl}#page=2` : ''
  const observations = report?.observations ?? []
  const showFallbackNotice = Boolean(report && (report.isCachedFallback || report.warning))

  return (
    <div className="extension-page live-data-page">
      <section className="extension-card">
        <div className="live-data-toolbar">
          <div className="extension-toolbar-copy">
            <span className="panel-eyebrow">OFFICIAL DAILY MARKET REPORT</span>
            <h2>Rice prices</h2>
            <p>Latest rice prices published by the Central Bank of Sri Lanka for the markets covered in its daily report.</p>
          </div>
          <button
            type="button"
            className="extension-secondary-button"
            onClick={() => void refresh(false, true)}
            disabled={refreshing}
          >
            {refreshing ? 'Checking…' : 'Check for today’s report'}
          </button>
        </div>

        {error && (
          <div className="extension-alert extension-alert-error" role="alert">
            {error}{report && ' Showing the last report that was successfully retrieved.'}
          </div>
        )}
        {loading && !report && <p className="live-data-empty" role="status">Checking the CBSL report page…</p>}
        {!loading && !report && !error && <p className="live-data-empty">No CBSL daily price report is currently available.</p>}

        {report && (
          <>
            {showFallbackNotice && (
              <div className="market-report-notice" role="status">
                <strong>Showing latest recorded CBSL market prices (live CBSL portal temporarily restricted automated access).</strong>
                <span>{report.warning ?? 'Displaying the most recent verified market price observations stored in the platform.'}</span>
              </div>
            )}

            <div className="market-report-summary">
              <div>
                <span className="panel-eyebrow">
                  {report.isCachedFallback ? 'LATEST RECORDED OBSERVATIONS' : 'LATEST AVAILABLE REPORT'}
                </span>
                <strong>{formatDate(report.reportDate)}</strong>
                <small>Checked {formatTime(report.checkedAtUtc)}</small>
              </div>
              {report.pdfUrl ? (
                <a className="extension-primary-button market-report-open" href={report.pdfUrl} target="_blank" rel="noreferrer">
                  Open full report
                </a>
              ) : (
                <a className="extension-secondary-button market-report-open" href={report.sourceUrl} target="_blank" rel="noreferrer">
                  Visit CBSL price portal
                </a>
              )}
            </div>

            {!showFallbackNotice && (
              <div className="market-report-notice">
                <strong>Published daily, not live trading prices.</strong>
                <span>{report.updateNote} If a new report has not yet been published today, this shows the latest available date.</span>
              </div>
            )}

            {safePdfUrl && (
              <iframe
                className="market-report-frame"
                src={safePdfUrl}
                title={`CBSL Daily Price Report for ${formatDate(report.reportDate)}`}
                loading="lazy"
              />
            )}

            {observations.length > 0 && (
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Rice variety</th>
                      <th>Market</th>
                      <th>Grade</th>
                      <th>Price type</th>
                      <th>Price</th>
                      <th>Observation date</th>
                      <th>Source</th>
                    </tr>
                  </thead>
                  <tbody>
                    {observations.map((item) => (
                      <tr key={item.id}>
                        <td>
                          <span className="table-primary">{item.riceVarietyName}</span>
                          <span className="table-secondary">{item.varietyCode}</span>
                        </td>
                        <td>{item.market}</td>
                        <td>{item.grade}</td>
                        <td>
                          <span className="status-badge">
                            <i />
                            {item.priceType}
                          </span>
                        </td>
                        <td><strong>{formatPrice(item.value, item.currency, item.unit)}</strong></td>
                        <td>{formatDate(item.observationDate)}</td>
                        <td>{item.source ?? report.sourceName}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <p className="live-data-attribution">
              Source: <a href={report.sourceUrl} target="_blank" rel="noreferrer">{report.sourceName} Daily Price Report</a>.
              The report contains the official price tables, including all rice rows and selected markets.
            </p>
          </>
        )}
      </section>
    </div>
  )
}
