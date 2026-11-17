import { useCallback, useEffect, useState } from 'react'

type LatestMarketPriceReport = {
  reportDate: string
  pdfUrl: string
  sourceUrl: string
  sourceName: string
  updateNote: string
  checkedAtUtc: string
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

export default function MarketPricesPage() {
  const [report, setReport] = useState<LatestMarketPriceReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const refresh = useCallback(async (background = false) => {
    if (!background) setRefreshing(true)
    setError('')
    try {
      const response = await fetch('/api/market-prices', { credentials: 'include' })
      if (!response.ok) {
        throw new Error(`The CBSL price report could not be checked (${response.status}).`)
      }
      const latest = await response.json() as LatestMarketPriceReport
      const pdfUrl = new URL(latest.pdfUrl)
      if (pdfUrl.protocol !== 'https:' || pdfUrl.hostname !== 'www.cbsl.gov.lk') {
        throw new Error('The CBSL report page returned an unexpected report link.')
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

  const safePdfUrl = report ? `${report.pdfUrl}#page=2` : ''

  return (
    <div className="extension-page live-data-page">
      <section className="extension-card">
        <div className="live-data-toolbar">
          <div className="extension-toolbar-copy">
            <span className="panel-eyebrow">OFFICIAL DAILY MARKET REPORT</span>
            <h2>Rice prices</h2>
            <p>Latest rice prices published by the Central Bank of Sri Lanka for the markets covered in its daily report.</p>
          </div>
          <button type="button" className="extension-secondary-button" onClick={() => void refresh()} disabled={refreshing}>
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

        {report && safePdfUrl && (
          <>
            <div className="market-report-summary">
              <div>
                <span className="panel-eyebrow">LATEST AVAILABLE REPORT</span>
                <strong>{formatDate(report.reportDate)}</strong>
                <small>Checked {formatTime(report.checkedAtUtc)}</small>
              </div>
              <a className="extension-primary-button market-report-open" href={report.pdfUrl} target="_blank" rel="noreferrer">
                Open full report
              </a>
            </div>
            <div className="market-report-notice">
              <strong>Published daily, not live trading prices.</strong>
              <span>{report.updateNote} If a new report has not yet been published today, this shows the latest available date.</span>
            </div>
            <iframe
              className="market-report-frame"
              src={safePdfUrl}
              title={`CBSL Daily Price Report for ${formatDate(report.reportDate)}`}
              loading="lazy"
            />
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
