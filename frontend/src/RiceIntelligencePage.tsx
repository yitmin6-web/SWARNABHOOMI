import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  SRI_LANKA_PROVINCES,
  getDistrictsForProvince,
} from './sriLankaLocations'

type ResourceRuleItem = {
  id: string
  category: string
  versionName: string
  seedPaddyKgPerHectare: number
  ureaKgPerHectare: number
  tspKgPerHectare: number
  mopKgPerHectare: number
  unit: string
  sourceReference: string
  effectiveFrom: string
  effectiveTo: string | null
  status: string
  isActive: boolean
  version: number
  reviewedBy: string | null
  reviewedAt: string | null
  changeReason: string | null
  notes: string | null
  createdAt: string
}

type FieldInputItem = {
  fieldId: string
  fieldName: string
  farmId: string
  farmName: string
  farmerId: string
  farmerName: string
  district: string | null
  province: string | null
  areaHectares: number
  verificationStatus: string
  activeVarietyName: string | null
  activeSeason: string | null
  seedPaddyKg: number
  ureaKg: number
  ureaBags: number
  tspKg: number
  tspBags: number
  mopKg: number
  mopBags: number
  totalFertilizerBags: number
}

type FieldHarvestForecastItem = {
  forecastId: string
  cultivationId: string
  fieldId: string
  fieldName: string
  farmId: string
  farmName: string
  farmerId: string
  farmerName: string
  district: string | null
  province: string | null
  areaHectares: number
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  maturityDays: number
  season: string
  plantingDate: string
  expectedHarvestDate: string
  harvestMonth: string
  growthStage: string
  verificationStatus: string
  verificationMultiplier: number
  weatherMultiplier: number
  weatherSummary: string | null
  confidenceCategory: string
  forecastedYieldKg: number
  forecastedYieldMt: number
  updatedAt: string
}

type DistrictRollupItem = {
  district: string
  province: string | null
  forecastedSupplyMt: number
  verifiedSupplyMt: number
  pendingSupplyMt: number
  activeDemandMt: number
  surplusOrDeficitMt: number
  status: string
  cultivatedHectares: number
  fieldsCount: number
  availableStockMt: number
  actualHarvestedMt: number
}

type MonthlyScheduleItem = {
  month: string
  monthName: string
  forecastedSupplyMt: number
  fieldsCount: number
  varietiesBreakdown: string[]
}

type SummaryTotalsItem = {
  totalForecastedMt: number
  totalActiveDemandMt: number
  netSurplusOrDeficitMt: number
  totalCultivatedAreaHectares: number
  totalFieldsCount: number
  verifiedSharePercentage: number
  totalAvailableStockMt: number
  averageForecastAccuracyPercent: number | null
}

type RiceIntelligenceOverview = {
  activeRule: ResourceRuleItem | null
  fieldInputs: FieldInputItem[]
  fieldForecasts: FieldHarvestForecastItem[]
  districtRollups: DistrictRollupItem[]
  monthlySchedule: MonthlyScheduleItem[]
  summaryTotals: SummaryTotalsItem
}

type SeasonOption = {
  id: string
  name: string
  year: number
  status: string
  isActive: boolean
}

type VarietyOption = {
  id: string
  name: string
  varietyCode: string
}

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, credentials: 'include' })
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as {
      error?: string
      detail?: string
    } | null
    throw new Error(body?.error || body?.detail || `Request failed (${response.status}).`)
  }
  return response.json() as Promise<T>
}

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return '—'
  const date = new Date(`${dateStr}T00:00:00`)
  return Number.isNaN(date.getTime())
    ? dateStr
    : new Intl.DateTimeFormat('en', { dateStyle: 'medium' }).format(date)
}

export default function RiceIntelligencePage({ role }: { role: string }) {
  const [data, setData] = useState<RiceIntelligenceOverview | null>(null)
  const [loading, setLoading] = useState(true)
  const [recalculating, setRecalculating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  // Filters
  const [selectedSeason, setSelectedSeason] = useState('')
  const [selectedProvince, setSelectedProvince] = useState('')
  const [selectedDistrict, setSelectedDistrict] = useState('')
  const [selectedVarietyId, setSelectedVarietyId] = useState('')

  // Reference options
  const [seasons, setSeasons] = useState<SeasonOption[]>([])
  const [varieties, setVarieties] = useState<VarietyOption[]>([])
  const [allRules, setAllRules] = useState<ResourceRuleItem[]>([])
  const [showRuleModal, setShowRuleModal] = useState(false)
  const [showHistory, setShowHistory] = useState(false)

  // Rule creation state
  const [ruleVersionName, setRuleVersionName] = useState('')
  const [ruleSeedKg, setRuleSeedKg] = useState('100')
  const [ruleUreaKg, setRuleUreaKg] = useState('225')
  const [ruleTspKg, setRuleTspKg] = useState('55')
  const [ruleMopKg, setRuleMopKg] = useState('60')
  const [ruleSource, setRuleSource] = useState('Department of Agriculture Sri Lanka (DOA)')
  const [ruleReason, setRuleReason] = useState('Annual revision')
  const [ruleNotes, setRuleNotes] = useState('')
  const [publishingRule, setPublishingRule] = useState(false)

  const isFarmer = role === 'Farmer'
  const isBuyer = role === 'Buyer'
  const isAdminOrGov = ['SuperAdmin', 'Developer', 'Government'].includes(role)

  const availableDistricts = useMemo(() => {
    return getDistrictsForProvince(selectedProvince)
  }, [selectedProvince])

  const fetchOverview = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const params = new URLSearchParams()
      if (selectedSeason) params.append('season', selectedSeason)
      if (selectedProvince) params.append('province', selectedProvince)
      if (selectedDistrict) params.append('district', selectedDistrict)
      if (selectedVarietyId) params.append('varietyId', selectedVarietyId)

      const url = `/api/intelligence/forecasts${params.toString() ? `?${params.toString()}` : ''}`
      const result = await requestJson<RiceIntelligenceOverview>(url)
      setData(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load intelligence data.')
    } finally {
      setLoading(false)
    }
  }, [selectedSeason, selectedProvince, selectedDistrict, selectedVarietyId])

  const fetchReferences = useCallback(async () => {
    try {
      const [seasonList, varietyList, ruleList] = await Promise.all([
        requestJson<SeasonOption[]>('/api/seasons').catch(() => []),
        requestJson<VarietyOption[]>('/api/varieties').catch(() => []),
        isAdminOrGov
          ? requestJson<ResourceRuleItem[]>('/api/intelligence/resource-rules').catch(() => [])
          : Promise.resolve([]),
      ])
      setSeasons(seasonList)
      setVarieties(varietyList)
      setAllRules(ruleList)
    } catch {
      // Non-critical reference failure
    }
  }, [isAdminOrGov])

  useEffect(() => {
    void fetchOverview()
  }, [fetchOverview])

  useEffect(() => {
    void fetchReferences()
  }, [fetchReferences])

  const handleRecalculate = async () => {
    setRecalculating(true)
    setError(null)
    setSuccess(null)
    try {
      const result = await requestJson<{ updatedCount: number }>(
        '/api/intelligence/forecasts/recalculate',
        { method: 'POST' }
      )
      setSuccess(`Successfully recalculated yield forecasts for ${result.updatedCount} cultivation cycle(s).`)
      await fetchOverview()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Recalculation failed.')
    } finally {
      setRecalculating(false)
    }
  }

  const handlePublishRule = async (event: FormEvent) => {
    event.preventDefault()
    setPublishingRule(true)
    setError(null)
    setSuccess(null)
    try {
      const body = {
        versionName: ruleVersionName.trim(),
        seedPaddyKgPerHectare: Number(ruleSeedKg),
        ureaKgPerHectare: Number(ruleUreaKg),
        tspKgPerHectare: Number(ruleTspKg),
        mopKgPerHectare: Number(ruleMopKg),
        sourceReference: ruleSource.trim(),
        changeReason: ruleReason.trim(),
        notes: ruleNotes.trim(),
        isActive: true,
      }

      const created = await requestJson<ResourceRuleItem>('/api/intelligence/resource-rules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })

      setSuccess(`Published Agronomic Rule Version ${created.version} (${created.versionName}) and updated forecasts.`)
      setShowRuleModal(false)
      setRuleVersionName('')
      setRuleNotes('')
      await Promise.all([fetchOverview(), fetchReferences()])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to publish rule.')
    } finally {
      setPublishingRule(false)
    }
  }

  // Calculate Farmer Totals for footer row
  const farmerTotals = useMemo(() => {
    if (!data?.fieldInputs.length) return null
    return data.fieldInputs.reduce(
      (acc, item) => ({
        areaHectares: acc.areaHectares + item.areaHectares,
        seedPaddyKg: acc.seedPaddyKg + item.seedPaddyKg,
        ureaKg: acc.ureaKg + item.ureaKg,
        ureaBags: acc.ureaBags + item.ureaBags,
        tspKg: acc.tspKg + item.tspKg,
        tspBags: acc.tspBags + item.tspBags,
        mopKg: acc.mopKg + item.mopKg,
        mopBags: acc.mopBags + item.mopBags,
        totalFertilizerBags: acc.totalFertilizerBags + item.totalFertilizerBags,
      }),
      {
        areaHectares: 0,
        seedPaddyKg: 0,
        ureaKg: 0,
        ureaBags: 0,
        tspKg: 0,
        tspBags: 0,
        mopKg: 0,
        mopBags: 0,
        totalFertilizerBags: 0,
      }
    )
  }, [data?.fieldInputs])

  // Max value for CSS horizontal bar charts
  const maxChartValue = useMemo(() => {
    if (!data?.districtRollups.length) return 100
    const maxVal = Math.max(
      ...data.districtRollups.map((r) => Math.max(r.forecastedSupplyMt, r.activeDemandMt))
    )
    return maxVal > 0 ? maxVal * 1.15 : 100
  }, [data?.districtRollups])

  const activeRule = data?.activeRule

  return (
    <div className="extension-page intelligence-page">
      {/* Top Toolbar */}
      <section className="extension-card intelligence-hero-card">
        <div className="extension-toolbar">
          <div className="extension-toolbar-copy">
            <span className="panel-eyebrow">RICE INTELLIGENCE ENGINE</span>
            <h2>Agronomic Rules & Harvest Yield Forecasting</h2>
            <p>
              {isFarmer
                ? 'Automated seed & fertilizer calculations based on Department of Agriculture standards, plus harvest yield projections for your mapped fields.'
                : isBuyer
                  ? 'Anonymized regional paddy supply forecasts, active market demand matching, and surplus/deficit balance analytics across Sri Lanka.'
                  : 'Official agronomic input rule management, district supply vs. demand balance, and automated harvest forecasting.'}
            </p>
          </div>

          <div className="toolbar-button-group">
            {!isBuyer && (
              <button
                type="button"
                className="extension-secondary-button"
                onClick={handleRecalculate}
                disabled={recalculating || loading}
              >
                {recalculating ? 'Recalculating…' : isFarmer ? 'Recalculate My Forecasts' : 'Recalculate All Forecasts'}
              </button>
            )}

            {isAdminOrGov && (
              <button
                type="button"
                className="extension-primary-button"
                onClick={() => setShowRuleModal(!showRuleModal)}
              >
                {showRuleModal ? 'Close Form' : 'Publish New Rule'}
              </button>
            )}
          </div>
        </div>

        {error && <div className="extension-alert extension-alert-error">{error}</div>}
        {success && <div className="extension-alert extension-alert-success">{success}</div>}
      </section>

      {/* Active Agronomic Input Rule Banner */}
      {activeRule && (
        <section className="extension-card rule-banner-card">
          <div className="rule-banner-header">
            <div>
              <span className="panel-eyebrow">ACTIVE AGRONOMIC STANDARD</span>
              <h3>{activeRule.versionName}</h3>
              <p className="form-subtext">
                Standardized by {activeRule.sourceReference} · Effective from {formatDate(activeRule.effectiveFrom)}
                {activeRule.notes && ` — ${activeRule.notes}`}
              </p>
            </div>
            {isAdminOrGov && allRules.length > 1 && (
              <button
                type="button"
                className="text-link"
                onClick={() => setShowHistory(!showHistory)}
              >
                {showHistory ? 'Hide Version History' : `Version History (${allRules.length})`}
              </button>
            )}
          </div>

          <div className="rule-rates-strip">
            <div className="rule-rate-chip">
              <span className="rate-label">Seed Paddy</span>
              <strong className="rate-value">{activeRule.seedPaddyKgPerHectare} <small>kg/ha</small></strong>
              <span className="rate-subtext">Certified seed requirement</span>
            </div>

            <div className="rule-rate-chip chip-urea">
              <span className="rate-label">Urea (Nitrogen)</span>
              <strong className="rate-value">{activeRule.ureaKgPerHectare} <small>kg/ha</small></strong>
              <span className="rate-subtext">~{(activeRule.ureaKgPerHectare / 50).toFixed(1)} bags (50kg)</span>
            </div>

            <div className="rule-rate-chip chip-tsp">
              <span className="rate-label">TSP (Phosphorus)</span>
              <strong className="rate-value">{activeRule.tspKgPerHectare} <small>kg/ha</small></strong>
              <span className="rate-subtext">~{(activeRule.tspKgPerHectare / 50).toFixed(1)} bags (50kg)</span>
            </div>

            <div className="rule-rate-chip chip-mop">
              <span className="rate-label">MOP (Potassium)</span>
              <strong className="rate-value">{activeRule.mopKgPerHectare} <small>kg/ha</small></strong>
              <span className="rate-subtext">~{(activeRule.mopKgPerHectare / 50).toFixed(1)} bags (50kg)</span>
            </div>
          </div>
        </section>
      )}

      {/* Admin/Gov: Version History Drawer */}
      {isAdminOrGov && showHistory && (
        <section className="extension-card">
          <div className="section-header-compact">
            <h3>DOA Agronomic Rule Versions</h3>
            <span className="record-count">{allRules.length} versions published</span>
          </div>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>VERSION</th>
                  <th>STANDARD NAME</th>
                  <th>SEED PADDY</th>
                  <th>UREA</th>
                  <th>TSP</th>
                  <th>MOP</th>
                  <th>STATUS</th>
                  <th>EFFECTIVE</th>
                </tr>
              </thead>
              <tbody>
                {allRules.map((r) => (
                  <tr key={r.id}>
                    <td><strong className="code-chip">v{r.version}</strong></td>
                    <td><strong className="table-primary">{r.versionName}</strong><span className="table-secondary">{r.changeReason || '—'}</span></td>
                    <td>{r.seedPaddyKgPerHectare} kg/ha</td>
                    <td>{r.ureaKgPerHectare} kg/ha</td>
                    <td>{r.tspKgPerHectare} kg/ha</td>
                    <td>{r.mopKgPerHectare} kg/ha</td>
                    <td><span className={`status-badge ${r.isActive ? 'status-positive' : 'status-neutral'}`}>{r.status}</span></td>
                    <td>{formatDate(r.effectiveFrom)}{r.effectiveTo ? ` — ${formatDate(r.effectiveTo)}` : ' (Current)'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Admin/Gov: Publish Rule Modal / Panel */}
      {isAdminOrGov && showRuleModal && (
        <section className="extension-card rule-mgmt-panel">
          <div className="section-header-compact">
            <div>
              <span className="panel-eyebrow">GOVERNMENT / DOA PUBLISHING</span>
              <h3>Publish New Agronomic Input Rule</h3>
            </div>
            <button type="button" className="text-link" onClick={() => setShowRuleModal(false)}>Close</button>
          </div>

          <form onSubmit={handlePublishRule} className="extension-form rule-publish-form">
            <div className="form-row-2col">
              <label>
                Standard Name *
                <input
                  type="text"
                  required
                  placeholder="e.g. DOA Irrigated Paddy Standard (2027 Yala)"
                  value={ruleVersionName}
                  onChange={(e) => setRuleVersionName(e.target.value)}
                />
              </label>

              <label>
                Source Authority *
                <input
                  type="text"
                  required
                  value={ruleSource}
                  onChange={(e) => setRuleSource(e.target.value)}
                />
              </label>
            </div>

            <div className="rule-form-rates-grid">
              <label>
                Seed Paddy (kg/ha) *
                <input
                  type="number"
                  step="0.1"
                  required
                  min="1"
                  value={ruleSeedKg}
                  onChange={(e) => setRuleSeedKg(e.target.value)}
                />
              </label>

              <label>
                Urea (kg/ha) *
                <input
                  type="number"
                  step="0.1"
                  required
                  min="1"
                  value={ruleUreaKg}
                  onChange={(e) => setRuleUreaKg(e.target.value)}
                />
              </label>

              <label>
                TSP (kg/ha) *
                <input
                  type="number"
                  step="0.1"
                  required
                  min="1"
                  value={ruleTspKg}
                  onChange={(e) => setRuleTspKg(e.target.value)}
                />
              </label>

              <label>
                MOP (kg/ha) *
                <input
                  type="number"
                  step="0.1"
                  required
                  min="1"
                  value={ruleMopKg}
                  onChange={(e) => setRuleMopKg(e.target.value)}
                />
              </label>
            </div>

            <div className="form-row-2col">
              <label>
                Reason for Change
                <input
                  type="text"
                  placeholder="e.g. Revised DOA fertilizer subsidy guideline"
                  value={ruleReason}
                  onChange={(e) => setRuleReason(e.target.value)}
                />
              </label>

              <label>
                Agronomic Notes
                <input
                  type="text"
                  placeholder="e.g. Recommendation applies for dry and intermediate zones"
                  value={ruleNotes}
                  onChange={(e) => setRuleNotes(e.target.value)}
                />
              </label>
            </div>

            <div className="rule-publish-actions">
              <span className="form-subtext">Publishing will set this standard as Active and automatically recalculate all forecasts.</span>
              <button
                type="submit"
                className="extension-primary-button"
                disabled={publishingRule}
              >
                {publishingRule ? 'Publishing…' : 'Publish & Recalculate'}
              </button>
            </div>
          </form>
        </section>
      )}

      {/* Filter Strip (for Buyer, Officer, Admin/Gov) */}
      {!isFarmer && (
        <section className="cultivation-filter-strip">
          <label>
            Season:
            <select
              value={selectedSeason}
              onChange={(e) => setSelectedSeason(e.target.value)}
            >
              <option value="">All Seasons</option>
              {seasons.map((s) => (
                <option key={s.id} value={s.name}>
                  {s.name} {s.isActive ? '(Active)' : ''}
                </option>
              ))}
            </select>
          </label>

          <label>
            Province:
            <select
              value={selectedProvince}
              onChange={(e) => {
                setSelectedProvince(e.target.value)
                setSelectedDistrict('')
              }}
            >
              <option value="">All Provinces</option>
              {SRI_LANKA_PROVINCES.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </label>

          <label>
            District:
            <select
              value={selectedDistrict}
              onChange={(e) => setSelectedDistrict(e.target.value)}
            >
              <option value="">All Districts</option>
              {availableDistricts.map((d) => (
                <option key={d} value={d}>{d}</option>
              ))}
            </select>
          </label>

          <label>
            Variety:
            <select
              value={selectedVarietyId}
              onChange={(e) => setSelectedVarietyId(e.target.value)}
            >
              <option value="">All Varieties</option>
              {varieties.map((v) => (
                <option key={v.id} value={v.id}>{v.name} ({v.varietyCode})</option>
              ))}
            </select>
          </label>

          {(selectedSeason || selectedProvince || selectedDistrict || selectedVarietyId) && (
            <button
              type="button"
              className="text-link"
              onClick={() => {
                setSelectedSeason('')
                setSelectedProvince('')
                setSelectedDistrict('')
                setSelectedVarietyId('')
              }}
            >
              Reset filters
            </button>
          )}
        </section>
      )}

      {/* Summary KPI Cards */}
      {data?.summaryTotals && (
        <section className="intelligence-kpi-grid">
          <div className="intelligence-kpi-card">
            <span className="kpi-label">TOTAL FORECASTED YIELD</span>
            <strong className="kpi-value">{data.summaryTotals.totalForecastedMt.toLocaleString()} <small>MT</small></strong>
            <span className="kpi-foot">{isFarmer ? 'Across your registered fields' : 'Projected harvest yield'}</span>
          </div>

          <div className="intelligence-kpi-card">
            <span className="kpi-label">AVAILABLE PADDY STOCK</span>
            <strong className="kpi-value">
              {(data.summaryTotals.totalAvailableStockMt ?? 0).toLocaleString()} <small>MT</small>
            </strong>
            <span className="kpi-foot">{isFarmer ? 'In your stored inventory' : 'Live post-harvest stored inventory'}</span>
          </div>

          {!isFarmer && (
            <div className="intelligence-kpi-card">
              <span className="kpi-label">ACTIVE BUYER DEMAND</span>
              <strong className="kpi-value">{data.summaryTotals.totalActiveDemandMt.toLocaleString()} <small>MT</small></strong>
              <span className="kpi-foot">Submitted procurement targets</span>
            </div>
          )}

          {!isFarmer && (
            <div className={`intelligence-kpi-card ${data.summaryTotals.netSurplusOrDeficitMt >= 0 ? 'kpi-surplus' : 'kpi-deficit'}`}>
              <span className="kpi-label">NET SUPPLY BALANCE</span>
              <strong className="kpi-value">
                {data.summaryTotals.netSurplusOrDeficitMt >= 0 ? '+' : ''}
                {data.summaryTotals.netSurplusOrDeficitMt.toLocaleString()} <small>MT</small>
              </strong>
              <span className="kpi-foot">
                {data.summaryTotals.netSurplusOrDeficitMt >= 0 ? 'National Surplus Position' : 'Deficit — Additional Supply Needed'}
              </span>
            </div>
          )}

          <div className="intelligence-kpi-card">
            <span className="kpi-label">TOTAL CULTIVATED AREA</span>
            <strong className="kpi-value">{data.summaryTotals.totalCultivatedAreaHectares.toLocaleString()} <small>ha</small></strong>
            <span className="kpi-foot">{data.summaryTotals.totalFieldsCount} mapped field plots</span>
          </div>

          <div className="intelligence-kpi-card">
            <span className="kpi-label">VERIFIED CONFIDENCE</span>
            <strong className="kpi-value">{data.summaryTotals.verifiedSharePercentage}%</strong>
            <span className="kpi-foot">Ground-verified GIS acreage</span>
          </div>

          {data.summaryTotals.averageForecastAccuracyPercent !== null && data.summaryTotals.averageForecastAccuracyPercent !== undefined && (
            <div className="intelligence-kpi-card">
              <span className="kpi-label">YIELD MODEL ACCURACY</span>
              <strong className="kpi-value">{data.summaryTotals.averageForecastAccuracyPercent.toFixed(1)}%</strong>
              <span className="kpi-foot">Average forecast vs. actual harvest fidelity</span>
            </div>
          )}
        </section>
      )}

      {/* ========================================================= */}
      {/* 1. FARMER VIEW: Farm Input Requirements Calculator       */}
      {/* ========================================================= */}
      {isFarmer && (
        <>
          <section className="extension-card">
            <div className="section-header-compact">
              <div>
                <span className="panel-eyebrow">INPUT CALCULATOR</span>
                <h3>My Farm Input Requirements</h3>
                <p className="form-subtext">Auto-calculated from your verified field boundaries and current DOA recommendations.</p>
              </div>
              <span className="record-count">{data?.fieldInputs.length || 0} fields mapped</span>
            </div>

            {loading ? (
              <div className="table-loading"><span className="loader" /> Calculating input requirements…</div>
            ) : !data?.fieldInputs.length ? (
              <div className="extension-empty">No mapped fields found. Map your field boundaries on the Satellite Map to calculate inputs.</div>
            ) : (
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>FIELD & FARM</th>
                      <th>AREA (HA)</th>
                      <th>ACTIVE CROP</th>
                      <th>SEED PADDY</th>
                      <th>UREA (46% N)</th>
                      <th>TSP (46% P)</th>
                      <th>MOP (60% K)</th>
                      <th>TOTAL BAGS</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.fieldInputs.map((item) => (
                      <tr key={item.fieldId}>
                        <td>
                          <strong className="table-primary">{item.fieldName}</strong>
                          <span className="table-secondary">{item.farmName} · {item.district || 'Sri Lanka'}</span>
                        </td>
                        <td><strong>{item.areaHectares.toFixed(2)} ha</strong></td>
                        <td>
                          {item.activeVarietyName ? (
                            <span className="code-chip">{item.activeVarietyName}</span>
                          ) : (
                            <span className="table-secondary">No active cycle</span>
                          )}
                        </td>
                        <td>
                          <strong>{item.seedPaddyKg.toLocaleString()} kg</strong>
                        </td>
                        <td>
                          <strong>{item.ureaKg.toLocaleString()} kg</strong>
                          <span className="table-secondary">{item.ureaBags} bags (50kg)</span>
                        </td>
                        <td>
                          <strong>{item.tspKg.toLocaleString()} kg</strong>
                          <span className="table-secondary">{item.tspBags} bags (50kg)</span>
                        </td>
                        <td>
                          <strong>{item.mopKg.toLocaleString()} kg</strong>
                          <span className="table-secondary">{item.mopBags} bags (50kg)</span>
                        </td>
                        <td>
                          <span className="badge-bags">{item.totalFertilizerBags} bags</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  {farmerTotals && (
                    <tfoot>
                      <tr className="table-totals-row">
                        <td><strong>TOTAL REQUIREMENTS</strong></td>
                        <td><strong>{farmerTotals.areaHectares.toFixed(2)} ha</strong></td>
                        <td>—</td>
                        <td><strong>{farmerTotals.seedPaddyKg.toLocaleString()} kg</strong></td>
                        <td><strong>{farmerTotals.ureaKg.toLocaleString()} kg ({farmerTotals.ureaBags} bags)</strong></td>
                        <td><strong>{farmerTotals.tspKg.toLocaleString()} kg ({farmerTotals.tspBags} bags)</strong></td>
                        <td><strong>{farmerTotals.mopKg.toLocaleString()} kg ({farmerTotals.mopBags} bags)</strong></td>
                        <td><strong className="badge-bags highlight-bags">{farmerTotals.totalFertilizerBags} bags</strong></td>
                      </tr>
                    </tfoot>
                  )}
                </table>
              </div>
            )}
          </section>

          {/* Farmer: Harvest Forecasts Table */}
          <section className="extension-card">
            <div className="section-header-compact">
              <div>
                <span className="panel-eyebrow">HARVEST CALENDAR & YIELD FORECASTS</span>
                <h3>My Projected Harvests</h3>
                <p className="form-subtext">Estimated harvest dates and expected paddy yields based on variety maturity and weather models.</p>
              </div>
              <span className="record-count">{data?.fieldForecasts.length || 0} active cycles</span>
            </div>

            {loading ? (
              <div className="table-loading"><span className="loader" /> Calculating yield models…</div>
            ) : !data?.fieldForecasts.length ? (
              <div className="extension-empty">No active cultivation cycles logged yet. Start a cultivation cycle in the Cultivation tab to view yield projections.</div>
            ) : (
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>FIELD & VARIETY</th>
                      <th>SEASON</th>
                      <th>SOWING DATE</th>
                      <th>EXPECTED HARVEST</th>
                      <th>GROWTH STAGE</th>
                      <th>VERIFICATION</th>
                      <th>PROJECTED YIELD</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.fieldForecasts.map((fc) => (
                      <tr key={fc.cultivationId}>
                        <td>
                          <strong className="table-primary">{fc.fieldName}</strong>
                          <span className="table-secondary">{fc.riceVarietyName} ({fc.varietyCode}) · {fc.areaHectares} ha</span>
                        </td>
                        <td>{fc.season}</td>
                        <td>{formatDate(fc.plantingDate)}</td>
                        <td>
                          <strong className="table-primary">{formatDate(fc.expectedHarvestDate)}</strong>
                          <span className="table-secondary">{fc.harvestMonth}</span>
                        </td>
                        <td>
                          <span className="code-chip">{fc.growthStage}</span>
                        </td>
                        <td>
                          <span className={`status-badge ${fc.verificationStatus === 'Verified' ? 'status-positive' : 'status-pending'}`}>
                            {fc.verificationStatus} ({fc.verificationMultiplier * 100}%)
                          </span>
                        </td>
                        <td>
                          <strong className="yield-highlight">{fc.forecastedYieldMt.toFixed(2)} MT</strong>
                          <span className="table-secondary">{fc.forecastedYieldKg.toLocaleString()} kg</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}

      {/* ========================================================= */}
      {/* 2. BUYER & OFFICER & ADMIN: Regional Surplus/Deficit      */}
      {/* ========================================================= */}
      {!isFarmer && (
        <>
          {/* CSS Horizontal Bar Chart: District Supply vs Demand */}
          <section className="extension-card">
            <div className="section-header-compact">
              <div>
                <span className="panel-eyebrow">REGIONAL SUPPLY VS DEMAND INTELLIGENCE</span>
                <h3>District Surplus / Deficit Analysis</h3>
                <p className="form-subtext">Visual comparison of forecasted paddy harvest supply against submitted buyer procurement demand.</p>
              </div>
              <div className="chart-legend">
                <span className="legend-item"><i className="legend-swatch supply-swatch" /> Forecasted Supply (MT)</span>
                <span className="legend-item"><i className="legend-swatch demand-swatch" /> Active Demand (MT)</span>
              </div>
            </div>

            {loading ? (
              <div className="table-loading"><span className="loader" /> Loading regional rollups…</div>
            ) : !data?.districtRollups.length ? (
              <div className="extension-empty">No regional cultivation or demand data matching selected filters.</div>
            ) : (
              <div className="surplus-deficit-chart">
                {data.districtRollups.map((rollup) => {
                  const supplyPct = Math.min(100, (rollup.forecastedSupplyMt / maxChartValue) * 100)
                  const demandPct = Math.min(100, (rollup.activeDemandMt / maxChartValue) * 100)

                  return (
                    <div className="chart-row" key={rollup.district}>
                      <div className="chart-district-label">
                        <strong>{rollup.district}</strong>
                        <span>{rollup.province || 'Sri Lanka'} · {rollup.fieldsCount} fields</span>
                      </div>

                      <div className="chart-bars-track">
                        {/* Supply Bar */}
                        <div className="bar-wrapper">
                          <div
                            className="chart-bar chart-bar-supply"
                            style={{ width: `${Math.max(4, supplyPct)}%` }}
                            title={`Supply: ${rollup.forecastedSupplyMt} MT`}
                          >
                            <span>{rollup.forecastedSupplyMt.toFixed(1)} MT</span>
                          </div>
                        </div>

                        {/* Demand Bar */}
                        <div className="bar-wrapper">
                          <div
                            className="chart-bar chart-bar-demand"
                            style={{ width: `${Math.max(4, demandPct)}%` }}
                            title={`Demand: ${rollup.activeDemandMt} MT`}
                          >
                            <span>{rollup.activeDemandMt.toFixed(1)} MT</span>
                          </div>
                        </div>
                      </div>

                      <div className="chart-status-col">
                        <span className={`balance-badge balance-${rollup.status.toLowerCase()}`}>
                          {rollup.status === 'Surplus' ? `+${rollup.surplusOrDeficitMt.toFixed(1)} MT Surplus` : rollup.status === 'Deficit' ? `${rollup.surplusOrDeficitMt.toFixed(1)} MT Deficit` : 'Balanced'}
                        </span>
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </section>

          {/* District Rollups Table */}
          <section className="extension-card">
            <div className="section-header-compact">
              <div>
                <span className="panel-eyebrow">DISTRICT ROLLUP AUDIT</span>
                <h3>Paddy Supply Rollup by District</h3>
              </div>
              <span className="record-count">{data?.districtRollups.length || 0} districts</span>
            </div>

            {data?.districtRollups.length ? (
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>DISTRICT</th>
                      <th>PROVINCE</th>
                      <th>CULTIVATED AREA</th>
                      <th>FIELDS</th>
                      <th>FORECASTED SUPPLY</th>
                      <th>GROUND-VERIFIED</th>
                      <th>AVAILABLE STOCK</th>
                      <th>ACTUAL HARVESTED</th>
                      <th>ACTIVE DEMAND</th>
                      <th>SUPPLY BALANCE</th>
                      <th>STATUS</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.districtRollups.map((r) => (
                      <tr key={r.district}>
                        <td><strong className="table-primary">{r.district}</strong></td>
                        <td>{r.province || '—'}</td>
                        <td>{r.cultivatedHectares.toFixed(2)} ha</td>
                        <td>{r.fieldsCount}</td>
                        <td><strong>{r.forecastedSupplyMt.toLocaleString()} MT</strong></td>
                        <td>{r.verifiedSupplyMt.toLocaleString()} MT</td>
                        <td><strong style={{ color: '#166534' }}>{(r.availableStockMt ?? 0).toFixed(2)} MT</strong></td>
                        <td>{(r.actualHarvestedMt ?? 0).toFixed(2)} MT</td>
                        <td>{r.activeDemandMt.toLocaleString()} MT</td>
                        <td>
                          <strong>
                            {r.surplusOrDeficitMt >= 0 ? `+${r.surplusOrDeficitMt.toFixed(2)}` : r.surplusOrDeficitMt.toFixed(2)} MT
                          </strong>
                        </td>
                        <td>
                          <span className={`status-badge ${r.status === 'Surplus' ? 'status-positive' : r.status === 'Deficit' ? 'status-error' : 'status-neutral'}`}>
                            {r.status}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </section>

          {/* Monthly Harvest Arrival Schedule */}
          {data?.monthlySchedule.length ? (
            <section className="extension-card">
              <div className="section-header-compact">
                <div>
                  <span className="panel-eyebrow">HARVEST TIMELINE</span>
                  <h3>Expected Monthly Paddy Arrivals</h3>
                </div>
                <span className="record-count">{data.monthlySchedule.length} months projected</span>
              </div>

              <div className="monthly-timeline-grid">
                {data.monthlySchedule.map((m) => (
                  <div className="monthly-timeline-card" key={m.month}>
                    <div className="monthly-card-head">
                      <strong>{m.monthName}</strong>
                      <span className="code-chip">{m.fieldsCount} fields</span>
                    </div>
                    <div className="monthly-card-tonnage">
                      <strong>{m.forecastedSupplyMt.toLocaleString()}</strong>
                      <small>MT Projected</small>
                    </div>
                    {m.varietiesBreakdown.length > 0 && (
                      <div className="monthly-varieties-list">
                        {m.varietiesBreakdown.map((vb, idx) => (
                          <span key={idx} className="variety-chip-small">{vb}</span>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </section>
          ) : null}

          {/* Admin / Government / Officer: Field-Level Audit Breakdown */}
          {!isBuyer && data?.fieldForecasts.length ? (
            <section className="extension-card">
              <div className="section-header-compact">
                <div>
                  <span className="panel-eyebrow">FIELD-LEVEL AUDIT</span>
                  <h3>Cultivation Field Forecasts Breakdown</h3>
                </div>
                <span className="record-count">{data.fieldForecasts.length} records</span>
              </div>

              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>FARMER</th>
                      <th>FIELD & FARM</th>
                      <th>DISTRICT</th>
                      <th>AREA (HA)</th>
                      <th>VARIETY</th>
                      <th>HARVEST DATE</th>
                      <th>VERIF MULTIPLIER</th>
                      <th>PROJECTED YIELD</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.fieldForecasts.map((fc) => (
                      <tr key={fc.cultivationId}>
                        <td>
                          <strong className="table-primary">{fc.farmerName}</strong>
                        </td>
                        <td>
                          <strong className="table-primary">{fc.fieldName}</strong>
                          <span className="table-secondary">{fc.farmName}</span>
                        </td>
                        <td>{fc.district || '—'}</td>
                        <td>{fc.areaHectares.toFixed(2)} ha</td>
                        <td>{fc.riceVarietyName} ({fc.varietyCode})</td>
                        <td>{formatDate(fc.expectedHarvestDate)}</td>
                        <td>
                          <span className={`status-badge ${fc.verificationMultiplier === 1 ? 'status-positive' : 'status-pending'}`}>
                            {fc.verificationMultiplier * 100}% ({fc.verificationStatus})
                          </span>
                        </td>
                        <td>
                          <strong className="yield-highlight">{fc.forecastedYieldMt.toFixed(2)} MT</strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          ) : null}
        </>
      )}
    </div>
  )
}
