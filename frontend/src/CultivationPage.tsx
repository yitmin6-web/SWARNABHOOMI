import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  SRI_LANKA_PROVINCES,
  getDistrictsForProvince,
} from './sriLankaLocations'

type SeasonItem = {
  id: string
  name: string
  year: number
  district: string | null
  agrarianServiceCentre: string | null
  plantingStart: string
  plantingEnd: string
  status: string
  isActive: boolean
  approvedBy: string | null
  approvedAt: string | null
  closedAt: string | null
  closureReason: string | null
}

type RiceVarietyItem = {
  id: string
  varietyCode: string
  name: string
  description: string | null
  maturityDays: number
  averageYieldKgPerHectare: number | null
  isActive: boolean
}

type FieldMapItem = {
  id: string
  name: string
  farmName: string
  areaHectares: number
  district: string | null
  province: string | null
  ownerName: string
  verificationStatus: string
}

type CultivationItem = {
  id: string
  farmerId: string
  farmerName: string
  fieldId: string
  fieldName: string
  farmId: string
  farmName: string
  district: string | null
  province: string | null
  areaHectares: number
  fieldVerificationStatus: string
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  maturityDays: number
  season: string
  plantingDate: string
  expectedHarvestDate: string
  status: string
  notes: string | null
  createdAt: string
}

type HarvestRecordItem = {
  id: string
  cultivationId: string
  farmerId: string
  farmerName: string
  fieldId: string
  fieldName: string
  farmName: string
  district: string | null
  province: string | null
  areaHectares: number
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  season: string
  plantingDate: string
  harvestDate: string
  actualPaddyKg: number
  forecastedYieldKg: number | null
  varianceKg: number | null
  variancePercent: number | null
  accuracyPercent: number | null
  quality: string | null
  lossesKg: number | null
  evidenceReference: string | null
  verificationStatus: string
  createdAtUtc: string
}

type StockRecordItem = {
  id: string
  location: string
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  grade: string
  commodityStage: string
  quantityKg: number
  asOfDate: string
  verificationStatus: string | null
  farmerName: string | null
  createdAtUtc: string
}

const GROWTH_STAGES = [
  'Planned',
  'Sown',
  'Vegetative',
  'Flowering',
  'Maturity',
  'Harvested',
  'Cancelled',
]

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

function calculateHarvestDate(plantingDateStr: string, maturityDays: number): string {
  if (!plantingDateStr || !maturityDays) return ''
  const date = new Date(`${plantingDateStr}T00:00:00`)
  if (Number.isNaN(date.getTime())) return ''
  date.setDate(date.getDate() + maturityDays)
  return date.toISOString().split('T')[0]
}

function getGrowthStageBadge(stage: string) {
  switch (stage.toLowerCase()) {
    case 'planned':
      return { label: 'Planned', className: 'status-neutral' }
    case 'sown':
      return { label: 'Sown', className: 'status-pending' }
    case 'vegetative':
      return { label: 'Vegetative', className: 'status-positive' }
    case 'flowering':
      return { label: 'Flowering', className: 'status-positive' }
    case 'maturity':
      return { label: 'Maturity', className: 'status-positive' }
    case 'harvested':
      return { label: 'Harvested', className: 'status-positive' }
    case 'cancelled':
      return { label: 'Cancelled', className: 'status-neutral' }
    default:
      return { label: stage, className: 'status-neutral' }
  }
}

function getAccuracyClass(accuracy: number | null | undefined): string {
  if (accuracy === null || accuracy === undefined) return 'accuracy-medium'
  if (accuracy >= 90) return 'accuracy-high'
  if (accuracy >= 75) return 'accuracy-medium'
  return 'accuracy-low'
}

export default function CultivationPage({ role }: { role: string }) {
  const [activeTab, setActiveTab] = useState<'cycles' | 'harvest-stock'>('cycles')
  const [seasons, setSeasons] = useState<SeasonItem[]>([])
  const [varieties, setVarieties] = useState<RiceVarietyItem[]>([])
  const [myFields, setMyFields] = useState<FieldMapItem[]>([])
  const [cultivations, setCultivations] = useState<CultivationItem[]>([])
  const [harvests, setHarvests] = useState<HarvestRecordItem[]>([])
  const [stocks, setStocks] = useState<StockRecordItem[]>([])

  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  // Form states for farmer: Log Cultivation
  const [fieldId, setFieldId] = useState('')
  const [seasonId, setSeasonId] = useState('')
  const [varietyId, setVarietyId] = useState('')
  const [sowingDate, setSowingDate] = useState(() => new Date().toISOString().split('T')[0])
  const [initialStage, setInitialStage] = useState('Planned')
  const [notes, setNotes] = useState('')

  // Form states for farmer: Log Harvest
  const [harvestCultivationId, setHarvestCultivationId] = useState('')
  const [harvestDate, setHarvestDate] = useState(() => new Date().toISOString().split('T')[0])
  const [actualPaddyKg, setActualPaddyKg] = useState('')
  const [moisturePercentage, setMoisturePercentage] = useState('13.5')
  const [qualityGrade, setQualityGrade] = useState('Grade A (<14% moisture)')
  const [lossesKg, setLossesKg] = useState('')
  const [harvestNotes, setHarvestNotes] = useState('')
  const [addToStock, setAddToStock] = useState(true)

  // Form states for Stock Entry / Adjustment
  const [showStockForm, setShowStockForm] = useState(false)
  const [stockVarietyId, setStockVarietyId] = useState('')
  const [stockLocation, setStockLocation] = useState('')
  const [stockStage, setStockStage] = useState('Paddy')
  const [stockGrade, setStockGrade] = useState('Grade A')
  const [stockQuantityKg, setStockQuantityKg] = useState('')
  const [stockAsOfDate, setStockAsOfDate] = useState(() => new Date().toISOString().split('T')[0])

  // Form states for Admin/Government: Create Season
  const [seasonName, setSeasonName] = useState('')
  const [seasonYear, setSeasonYear] = useState(() => new Date().getFullYear().toString())
  const [seasonDistrict, setSeasonDistrict] = useState('National')
  const [seasonStart, setSeasonStart] = useState(() => new Date().toISOString().split('T')[0])
  const [seasonEnd, setSeasonEnd] = useState(() => {
    const d = new Date()
    d.setMonth(d.getMonth() + 6)
    return d.toISOString().split('T')[0]
  })
  const [seasonIsActive, setSeasonIsActive] = useState(true)

  // Filter states for Admin/Officer Cultivation & Harvest Monitor
  const [filterSeason, setFilterSeason] = useState('')
  const [filterProvince, setFilterProvince] = useState('')
  const [filterDistrict, setFilterDistrict] = useState('')

  const isFarmer = role === 'Farmer'
  const isOfficer = role === 'Officer'
  const isAdminOrGov = ['SuperAdmin', 'Developer', 'Government'].includes(role)
  const canManageSeasons = isAdminOrGov

  // Active seasons for selection
  const activeSeasons = useMemo(() => seasons.filter((s) => s.isActive), [seasons])

  // Selected variety details
  const selectedVariety = useMemo(
    () => varieties.find((v) => v.id === varietyId),
    [varieties, varietyId]
  )

  // Estimated harvest date for live preview
  const estimatedHarvestDate = useMemo(() => {
    if (!sowingDate || !selectedVariety) return ''
    return calculateHarvestDate(sowingDate, selectedVariety.maturityDays)
  }, [sowingDate, selectedVariety])

  // Selected field details
  const selectedField = useMemo(
    () => myFields.find((f) => f.id === fieldId),
    [myFields, fieldId]
  )

  // Available districts for filter
  const filterDistricts = useMemo(
    () => getDistrictsForProvince(filterProvince),
    [filterProvince]
  )

  // Eligible cultivations for harvest logging (status !== 'Cancelled')
  const eligibleCultivations = useMemo(
    () => cultivations.filter((c) => c.status !== 'Cancelled'),
    [cultivations]
  )

  const selectedHarvestCultivation = useMemo(
    () => cultivations.find((c) => c.id === harvestCultivationId),
    [cultivations, harvestCultivationId]
  )

  // Farmer total stored stock in kg
  const totalStockKg = useMemo(
    () => stocks.reduce((sum, s) => sum + s.quantityKg, 0),
    [stocks]
  )

  const loadData = useCallback(async () => {
    const promises: [
      Promise<SeasonItem[]>,
      Promise<RiceVarietyItem[]>,
      Promise<CultivationItem[]>,
      Promise<{ fields: FieldMapItem[] }>,
      Promise<HarvestRecordItem[]>,
      Promise<StockRecordItem[]>
    ] = [
      requestJson<SeasonItem[]>('/api/seasons').catch(() => []),
      requestJson<RiceVarietyItem[]>('/api/rice-varieties').catch(() => []),
      requestJson<CultivationItem[]>('/api/cultivations').catch(() => []),
      isFarmer
        ? requestJson<{ fields: FieldMapItem[] }>('/api/field-map').catch(() => ({ fields: [] }))
        : Promise.resolve({ fields: [] }),
      role !== 'Buyer'
        ? requestJson<HarvestRecordItem[]>('/api/harvests').catch(() => [])
        : Promise.resolve([]),
      requestJson<StockRecordItem[]>('/api/stocks').catch(() => []),
    ]

    const [seasonList, varietyList, cultivationList, fieldData, harvestList, stockList] =
      await Promise.all(promises)
    return {
      seasonList,
      varietyList,
      cultivationList,
      fields: fieldData.fields || [],
      harvestList,
      stockList,
    }
  }, [isFarmer, role])

  const applyData = useCallback(
    (loaded: Awaited<ReturnType<typeof loadData>>) => {
      setSeasons(loaded.seasonList)
      setVarieties(loaded.varietyList)
      setCultivations(loaded.cultivationList)
      setHarvests(loaded.harvestList)
      setStocks(loaded.stockList)
      if (isFarmer) {
        setMyFields(loaded.fields)
        if (!fieldId && loaded.fields.length > 0) {
          setFieldId(loaded.fields[0].id)
        }
      }
      if (!seasonId && loaded.seasonList.length > 0) {
        const firstActive = loaded.seasonList.find((s) => s.isActive) || loaded.seasonList[0]
        setSeasonId(firstActive.id)
      }
      if (!varietyId && loaded.varietyList.length > 0) {
        const firstActiveVariety = loaded.varietyList.find((v) => v.isActive) || loaded.varietyList[0]
        setVarietyId(firstActiveVariety.id)
      }
      if (!harvestCultivationId && loaded.cultivationList.length > 0) {
        const eligible = loaded.cultivationList.find((c) => c.status !== 'Cancelled')
        if (eligible) setHarvestCultivationId(eligible.id)
      }
      if (!stockVarietyId && loaded.varietyList.length > 0) {
        setStockVarietyId(loaded.varietyList[0].id)
      }
    },
    [isFarmer, fieldId, seasonId, varietyId, harvestCultivationId, stockVarietyId]
  )

  useEffect(() => {
    let active = true
    void loadData()
      .then((loaded) => {
        if (active) applyData(loaded)
      })
      .catch((err: unknown) => {
        if (active) setError(err instanceof Error ? err.message : 'Unable to load cultivation data.')
      })
      .finally(() => {
        if (active) setLoading(false)
      })

    return () => {
      active = false
    }
  }, [loadData, applyData])

  const refreshData = () => {
    setLoading(true)
    setError('')
    void loadData()
      .then(applyData)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Unable to load cultivation data.'))
      .finally(() => setLoading(false))
  }

  // Handle Farmer: Log Cultivation
  const handleLogCultivation = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (!fieldId) {
      setError('Select a field from your registered fields.')
      return
    }
    if (!seasonId) {
      setError('Select an active cultivation season.')
      return
    }
    if (!varietyId) {
      setError('Select a rice variety.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const chosenSeason = seasons.find((s) => s.id === seasonId)
      await requestJson<CultivationItem>('/api/cultivations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fieldId,
          riceVarietyId: varietyId,
          seasonId,
          season: chosenSeason?.name,
          plantingDate: sowingDate,
          status: initialStage,
          notes: notes.trim() || null,
        }),
      })

      setNotice('Cultivation cycle logged successfully.')
      setNotes('')
      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to log cultivation.')
    } finally {
      setBusy(false)
    }
  }

  // Handle Farmer: Log Harvest
  const handleLogHarvest = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (!harvestCultivationId) {
      setError('Select a cultivation cycle to record harvest for.')
      return
    }
    const qty = Number(actualPaddyKg)
    if (Number.isNaN(qty) || qty <= 0) {
      setError('Provide a valid actual harvest quantity in kg.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const qualityStr = `${qualityGrade} (${moisturePercentage}% moisture)`
      const created = await requestJson<HarvestRecordItem>('/api/harvests', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          cultivationId: harvestCultivationId,
          actualHarvestKg: qty,
          harvestDate,
          quality: qualityStr,
          lossesKg: lossesKg ? Number(lossesKg) : null,
          notes: harvestNotes.trim() || null,
          addToStock,
        }),
      })

      const accuracyMsg = created.accuracyPercent !== null
        ? ` Forecast model accuracy: ${created.accuracyPercent.toFixed(1)}%.`
        : ''
      setNotice(`Harvest recorded successfully!${accuracyMsg} Growth stage advanced to 'Harvested'.`)
      setActualPaddyKg('')
      setLossesKg('')
      setHarvestNotes('')

      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to record harvest.')
    } finally {
      setBusy(false)
    }
  }

  // Handle Upsert Stock
  const handleUpsertStock = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (!stockVarietyId) {
      setError('Select a rice variety.')
      return
    }
    const qty = Number(stockQuantityKg)
    if (Number.isNaN(qty) || qty <= 0) {
      setError('Provide a valid stock quantity in kg.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const loc = stockLocation.trim() || (selectedField?.district || 'National')
      await requestJson<StockRecordItem>('/api/stocks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          location: loc,
          riceVarietyId: stockVarietyId,
          grade: stockGrade,
          commodityStage: stockStage,
          quantityKg: qty,
          asOfDate: stockAsOfDate,
        }),
      })

      setNotice(`Stock inventory updated successfully for ${loc}.`)
      setShowStockForm(false)
      setStockQuantityKg('')

      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update stock inventory.')
    } finally {
      setBusy(false)
    }
  }

  // Handle stage change (Farmer, Officer, Admin)
  const handleUpdateStage = async (cultivationId: string, newStage: string) => {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestJson<CultivationItem>(`/api/cultivations/${cultivationId}/stage`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ stage: newStage }),
      })
      setNotice(`Growth stage updated to ${newStage}.`)
      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update growth stage.')
    } finally {
      setBusy(false)
    }
  }

  // Handle Admin: Create Season
  const handleCreateSeason = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (!seasonName.trim()) {
      setError('Provide a season name.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestJson<SeasonItem>('/api/seasons', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: seasonName.trim(),
          year: Number(seasonYear) || new Date().getFullYear(),
          district: seasonDistrict.trim() || 'National',
          plantingStart: seasonStart,
          plantingEnd: seasonEnd,
          isActive: seasonIsActive,
        }),
      })

      setNotice(`Season '${seasonName.trim()}' created successfully.`)
      setSeasonName('')
      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create season.')
    } finally {
      setBusy(false)
    }
  }

  // Handle Admin: Toggle Season Active
  const handleToggleSeasonStatus = async (season: SeasonItem) => {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestJson<SeasonItem>(`/api/seasons/${season.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          isActive: !season.isActive,
          closureReason: season.isActive ? 'Season closed by administrator' : null,
        }),
      })

      setNotice(`Season '${season.name}' ${season.isActive ? 'closed' : 'activated'}.`)
      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to toggle season status.')
    } finally {
      setBusy(false)
    }
  }

  // Filtered cultivations for Admin / Officer
  const filteredCultivations = useMemo(() => {
    return cultivations.filter((c) => {
      if (filterSeason && c.season.toLowerCase() !== filterSeason.toLowerCase()) return false
      if (filterProvince && c.province !== filterProvince) return false
      if (filterDistrict && c.district !== filterDistrict) return false
      return true
    })
  }, [cultivations, filterSeason, filterProvince, filterDistrict])

  // Filtered harvests for Admin / Officer
  const filteredHarvests = useMemo(() => {
    return harvests.filter((h) => {
      if (filterSeason && h.season.toLowerCase() !== filterSeason.toLowerCase()) return false
      if (filterProvince && h.province !== filterProvince) return false
      if (filterDistrict && h.district !== filterDistrict) return false
      return true
    })
  }, [harvests, filterSeason, filterProvince, filterDistrict])

  // Average Forecast Accuracy for filtered harvests
  const averageAccuracy = useMemo(() => {
    const withAcc = filteredHarvests.filter((h) => h.accuracyPercent !== null && h.accuracyPercent !== undefined)
    if (withAcc.length === 0) return null
    return withAcc.reduce((sum, h) => sum + (h.accuracyPercent ?? 0), 0) / withAcc.length
  }, [filteredHarvests])

  return (
    <div className="extension-page cultivation-page">
      <div className="extension-card cultivation-hero-card">
        <div className="extension-toolbar">
          <div>
            <span className="panel-eyebrow">CULTIVATION CYCLES, HARVESTS & STOCK</span>
            <h2>{isFarmer ? 'Crop Planning, Harvest Logging & Stock' : 'Cultivation, Harvest & Stock Monitoring'}</h2>
            <p>
              Connect verified GPS field boundaries and rice varieties to official cropping seasons with real-time growth tracking, harvest yield variance analysis, and post-harvest stock monitoring.
            </p>
          </div>
          <button
            type="button"
            className="extension-secondary-button"
            onClick={refreshData}
            disabled={loading || busy}
          >
            {loading ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>

        {/* Subnavigation Tabs */}
        <div className="subnav-tabs">
          <button
            type="button"
            className={`subnav-tab ${activeTab === 'cycles' ? 'active' : ''}`}
            onClick={() => setActiveTab('cycles')}
          >
            Cultivation Cycles ({cultivations.length})
          </button>
          <button
            type="button"
            className={`subnav-tab ${activeTab === 'harvest-stock' ? 'active' : ''}`}
            onClick={() => setActiveTab('harvest-stock')}
          >
            Log Harvest & Stock ({harvests.length})
          </button>
        </div>

        {error && (
          <p className="extension-alert extension-alert-error" role="alert">
            {error}
          </p>
        )}
        {notice && (
          <p className="extension-alert extension-alert-success" role="status">
            {notice}
          </p>
        )}

        {/* ============================================================== */}
        {/* TAB 1: CULTIVATION CYCLES & SEASONS                           */}
        {/* ============================================================== */}
        {activeTab === 'cycles' && (
          <>
            {/* Farmer Portal: Start New Cultivation Cycle */}
            {isFarmer && (
              <div className="cultivation-farmer-grid">
                {myFields.length === 0 ? (
                  <div className="field-mapping-callout">
                    <span className="callout-icon">🌾</span>
                    <div>
                      <h4>No mapped fields found</h4>
                      <p>
                        You need at least one registered field polygon before logging a seasonal cultivation cycle.
                      </p>
                    </div>
                  </div>
                ) : (
                  <form className="extension-form cultivation-entry-form" onSubmit={(e) => void handleLogCultivation(e)}>
                    <h3>Log new cultivation cycle</h3>
                    <p className="form-subtext">Record sowing details for your registered field.</p>

                    <div className="form-row-2col">
                      <label>
                        Field plot
                        <select
                          value={fieldId}
                          onChange={(e) => setFieldId(e.target.value)}
                          required
                        >
                          {myFields.map((f) => (
                            <option key={f.id} value={f.id}>
                              {f.name} ({f.farmName} · {f.areaHectares.toFixed(2)} ha · {f.verificationStatus})
                            </option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Cropping season
                        <select
                          value={seasonId}
                          onChange={(e) => setSeasonId(e.target.value)}
                          required
                        >
                          {activeSeasons.length === 0 ? (
                            <option value="">No active seasons available</option>
                          ) : (
                            activeSeasons.map((s) => (
                              <option key={s.id} value={s.id}>
                                {s.name} ({formatDate(s.plantingStart)} — {formatDate(s.plantingEnd)})
                              </option>
                            ))
                          )}
                        </select>
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Rice variety
                        <select
                          value={varietyId}
                          onChange={(e) => setVarietyId(e.target.value)}
                          required
                        >
                          {varieties.map((v) => (
                            <option key={v.id} value={v.id}>
                              {v.name} ({v.varietyCode} · {v.maturityDays} days)
                            </option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Sowing / Planting date
                        <input
                          type="date"
                          value={sowingDate}
                          onChange={(e) => setSowingDate(e.target.value)}
                          required
                        />
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Initial growth stage
                        <select
                          value={initialStage}
                          onChange={(e) => setInitialStage(e.target.value)}
                        >
                          {GROWTH_STAGES.map((stage) => (
                            <option key={stage} value={stage}>
                              {stage}
                            </option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Notes & remarks (optional)
                        <input
                          type="text"
                          placeholder="e.g. Direct wet seeded, certified seed source"
                          value={notes}
                          onChange={(e) => setNotes(e.target.value)}
                          maxLength={1000}
                        />
                      </label>
                    </div>

                    {/* Telemetry Preview before submission */}
                    <div className="cultivation-preview-strip">
                      <div className="preview-item">
                        <span>Field Area</span>
                        <strong>{selectedField ? `${selectedField.areaHectares.toFixed(2)} ha` : '—'}</strong>
                      </div>
                      <div className="preview-item">
                        <span>Maturity Window</span>
                        <strong>{selectedVariety ? `${selectedVariety.maturityDays} days` : '—'}</strong>
                      </div>
                      <div className="preview-item highlight">
                        <span>Estimated Harvest Date</span>
                        <strong>{estimatedHarvestDate ? formatDate(estimatedHarvestDate) : '—'}</strong>
                      </div>
                    </div>

                    <button
                      type="submit"
                      className="extension-primary-button"
                      disabled={busy || !fieldId || !seasonId || !varietyId}
                    >
                      {busy ? 'Logging…' : 'Start Cultivation Cycle'}
                    </button>
                  </form>
                )}

                {/* Farmer: My Cultivation Cycles List */}
                <div className="cultivation-my-cycles">
                  <div className="section-header-compact">
                    <h3>My seasonal cycles</h3>
                    <span className="record-count">{cultivations.length} records</span>
                  </div>

                  {cultivations.length === 0 ? (
                    <p className="extension-empty">No active or historical cultivation records logged yet.</p>
                  ) : (
                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>FIELD & FARM</th>
                            <th>SEASON & VARIETY</th>
                            <th>SOWING DATE</th>
                            <th>EST. HARVEST</th>
                            <th>GROWTH STAGE</th>
                            <th>STAGE ACTION</th>
                          </tr>
                        </thead>
                        <tbody>
                          {cultivations.map((item) => {
                            const stageBadge = getGrowthStageBadge(item.status)
                            return (
                              <tr key={item.id}>
                                <td>
                                  <strong className="table-primary">{item.fieldName}</strong>
                                  <span className="table-secondary">
                                    {item.farmName} · {item.areaHectares.toFixed(2)} ha
                                  </span>
                                </td>
                                <td>
                                  <strong className="table-primary">{item.riceVarietyName}</strong>
                                  <span className="table-secondary">
                                    {item.season} ({item.maturityDays}d cycle)
                                  </span>
                                </td>
                                <td>{formatDate(item.plantingDate)}</td>
                                <td>
                                  <strong style={{ color: '#166534' }}>{formatDate(item.expectedHarvestDate)}</strong>
                                </td>
                                <td>
                                  <span className={`status-badge ${stageBadge.className}`}>
                                    <i />
                                    {item.status}
                                  </span>
                                </td>
                                <td>
                                  <select
                                    className="stage-select-compact"
                                    value={item.status}
                                    disabled={busy}
                                    onChange={(e) => void handleUpdateStage(item.id, e.target.value)}
                                    aria-label={`Update stage for ${item.fieldName}`}
                                  >
                                    {GROWTH_STAGES.map((st) => (
                                      <option key={st} value={st}>
                                        {st}
                                      </option>
                                    ))}
                                  </select>
                                </td>
                              </tr>
                            )
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Admin / Government Season Management */}
            {canManageSeasons && (
              <section className="season-management-section">
                <div className="season-mgmt-grid">
                  <form className="extension-form season-create-form" onSubmit={(e) => void handleCreateSeason(e)}>
                    <h3>Create Season</h3>
                    <p className="form-subtext">Define national or provincial cropping windows.</p>

                    <label>
                      Season name
                      <input
                        type="text"
                        placeholder="e.g. 2027/2028 Maha"
                        value={seasonName}
                        onChange={(e) => setSeasonName(e.target.value)}
                        required
                        maxLength={100}
                      />
                    </label>

                    <div className="form-row-2col">
                      <label>
                        Year
                        <input
                          type="number"
                          value={seasonYear}
                          onChange={(e) => setSeasonYear(e.target.value)}
                          min={2020}
                          max={2040}
                          required
                        />
                      </label>

                      <label>
                        Scope / District
                        <input
                          type="text"
                          value={seasonDistrict}
                          onChange={(e) => setSeasonDistrict(e.target.value)}
                          placeholder="National or District"
                          maxLength={120}
                        />
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Planting Start
                        <input
                          type="date"
                          value={seasonStart}
                          onChange={(e) => setSeasonStart(e.target.value)}
                          required
                        />
                      </label>

                      <label>
                        Planting End
                        <input
                          type="date"
                          value={seasonEnd}
                          onChange={(e) => setSeasonEnd(e.target.value)}
                          required
                        />
                      </label>
                    </div>

                    <label className="checkbox-row">
                      <input
                        type="checkbox"
                        checked={seasonIsActive}
                        onChange={(e) => setSeasonIsActive(e.target.checked)}
                      />
                      <span>Activate season immediately for farmers</span>
                    </label>

                    <button type="submit" className="extension-primary-button" disabled={busy}>
                      {busy ? 'Saving…' : 'Register Season'}
                    </button>
                  </form>

                  <div className="season-list-card">
                    <div className="section-header-compact">
                      <h3>Configured Seasons</h3>
                      <span className="record-count">{seasons.length} seasons</span>
                    </div>

                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>SEASON</th>
                            <th>PLANTING WINDOW</th>
                            <th>STATUS</th>
                            <th>ACTION</th>
                          </tr>
                        </thead>
                        <tbody>
                          {seasons.map((s) => (
                            <tr key={s.id}>
                              <td>
                                <strong className="table-primary">{s.name}</strong>
                                <span className="table-secondary">
                                  {s.year} · {s.district || 'National'}
                                </span>
                              </td>
                              <td>
                                {formatDate(s.plantingStart)} — {formatDate(s.plantingEnd)}
                              </td>
                              <td>
                                <span
                                  className={`status-badge ${s.isActive ? 'status-positive' : 'status-neutral'}`}
                                >
                                  <i />
                                  {s.status}
                                </span>
                              </td>
                              <td>
                                <button
                                  type="button"
                                  className={s.isActive ? 'user-reject-button' : 'user-approve-button'}
                                  disabled={busy}
                                  onClick={() => void handleToggleSeasonStatus(s)}
                                >
                                  {s.isActive ? 'Deactivate' : 'Activate'}
                                </button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              </section>
            )}

            {/* Cultivation Monitor for Admin & Officer */}
            {(isAdminOrGov || isOfficer) && (
              <section className="cultivation-monitor-section">
                <div className="section-header-compact" style={{ marginTop: '28px' }}>
                  <div>
                    <span className="panel-eyebrow">REGIONAL INTELLIGENCE</span>
                    <h3>National Cultivation Monitor</h3>
                  </div>
                  <span className="record-count">{filteredCultivations.length} field cycles</span>
                </div>

                {/* Filter strip */}
                <div className="cultivation-filter-strip">
                  <label>
                    Season
                    <select value={filterSeason} onChange={(e) => setFilterSeason(e.target.value)}>
                      <option value="">All Seasons</option>
                      {seasons.map((s) => (
                        <option key={s.id} value={s.name}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Province
                    <select
                      value={filterProvince}
                      onChange={(e) => {
                        setFilterProvince(e.target.value)
                        setFilterDistrict('')
                      }}
                    >
                      <option value="">All Provinces</option>
                      {SRI_LANKA_PROVINCES.map((prov) => (
                        <option key={prov} value={prov}>
                          {prov}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    District
                    <select
                      value={filterDistrict}
                      onChange={(e) => setFilterDistrict(e.target.value)}
                      disabled={!filterProvince && filterDistricts.length === 0}
                    >
                      <option value="">All Districts</option>
                      {filterDistricts.map((dist) => (
                        <option key={dist} value={dist}>
                          {dist}
                        </option>
                      ))}
                    </select>
                  </label>

                  {(filterSeason || filterProvince || filterDistrict) && (
                    <button
                      type="button"
                      className="extension-secondary-button"
                      onClick={() => {
                        setFilterSeason('')
                        setFilterProvince('')
                        setFilterDistrict('')
                      }}
                      style={{ alignSelf: 'flex-end', height: '36px' }}
                    >
                      Clear filters
                    </button>
                  )}
                </div>

                {filteredCultivations.length === 0 ? (
                  <p className="extension-empty" style={{ padding: '16px 0' }}>
                    No cultivation records found for the selected criteria.
                  </p>
                ) : (
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>FARMER</th>
                          <th>FIELD & FARM</th>
                          <th>LOCATION</th>
                          <th>AREA</th>
                          <th>VARIETY</th>
                          <th>SEASON</th>
                          <th>SOWING</th>
                          <th>EST. HARVEST</th>
                          <th>GROWTH STAGE</th>
                          <th>UPDATE STAGE</th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredCultivations.map((item) => {
                          const stageBadge = getGrowthStageBadge(item.status)
                          return (
                            <tr key={item.id}>
                              <td>
                                <strong className="table-primary">{item.farmerName}</strong>
                              </td>
                              <td>
                                <strong className="table-primary">{item.fieldName}</strong>
                                <span className="table-secondary">{item.farmName}</span>
                              </td>
                              <td>
                                {item.district || '—'}
                                <span className="table-secondary">{item.province || '—'}</span>
                              </td>
                              <td>
                                <strong>{item.areaHectares.toFixed(2)} ha</strong>
                              </td>
                              <td>
                                <strong className="table-primary">{item.riceVarietyName}</strong>
                                <span className="table-secondary">{item.varietyCode}</span>
                              </td>
                              <td>{item.season}</td>
                              <td>{formatDate(item.plantingDate)}</td>
                              <td>
                                <strong style={{ color: '#166534' }}>{formatDate(item.expectedHarvestDate)}</strong>
                              </td>
                              <td>
                                <span className={`status-badge ${stageBadge.className}`}>
                                  <i />
                                  {item.status}
                                </span>
                              </td>
                              <td>
                                <select
                                  className="stage-select-compact"
                                  value={item.status}
                                  disabled={busy}
                                  onChange={(e) => void handleUpdateStage(item.id, e.target.value)}
                                  aria-label={`Update stage for ${item.fieldName}`}
                                >
                                  {GROWTH_STAGES.map((st) => (
                                    <option key={st} value={st}>
                                      {st}
                                    </option>
                                  ))}
                                </select>
                              </td>
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            )}
          </>
        )}

        {/* ============================================================== */}
        {/* TAB 2: LOG HARVEST & STOCK MANAGEMENT                         */}
        {/* ============================================================== */}
        {activeTab === 'harvest-stock' && (
          <div className="harvest-stock-tab-container">
            {/* Farmer Harvest Logging Form */}
            {isFarmer && (
              <div className="cultivation-farmer-grid">
                {eligibleCultivations.length === 0 ? (
                  <div className="field-mapping-callout">
                    <span className="callout-icon">🌾</span>
                    <div>
                      <h4>No active cultivation cycles eligible for harvest</h4>
                      <p>
                        You must log and advance a cultivation cycle first before recording actual harvest yields.
                      </p>
                    </div>
                  </div>
                ) : (
                  <form className="extension-form cultivation-entry-form" onSubmit={(e) => void handleLogHarvest(e)}>
                    <h3>Log Actual Harvest & Close Cycle</h3>
                    <p className="form-subtext">Record final harvest output to calculate yield accuracy against projected models.</p>

                    <label>
                      Cultivation Cycle
                      <select
                        value={harvestCultivationId}
                        onChange={(e) => setHarvestCultivationId(e.target.value)}
                        required
                      >
                        {eligibleCultivations.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.fieldName} ({c.farmName}) · {c.riceVarietyName} · {c.season} · [{c.status}]
                          </option>
                        ))}
                      </select>
                    </label>

                    <div className="form-row-2col">
                      <label>
                        Actual Harvest Date
                        <input
                          type="date"
                          value={harvestDate}
                          onChange={(e) => setHarvestDate(e.target.value)}
                          required
                        />
                      </label>

                      <label>
                        Actual Paddy Harvested (kg)
                        <input
                          type="number"
                          placeholder="e.g. 4200"
                          value={actualPaddyKg}
                          onChange={(e) => setActualPaddyKg(e.target.value)}
                          min={1}
                          max={500000}
                          step={1}
                          required
                        />
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Quality Grade
                        <select
                          value={qualityGrade}
                          onChange={(e) => setQualityGrade(e.target.value)}
                        >
                          <option value="Grade A (<14% moisture)">Grade A (Premium · &lt;14% moisture)</option>
                          <option value="Grade B (14-16% moisture)">Grade B (Commercial · 14-16% moisture)</option>
                          <option value="Standard">Standard / Mill-Ready</option>
                          <option value="Wet Paddy (>16% moisture)">Wet Paddy (&gt;16% moisture)</option>
                        </select>
                      </label>

                      <label>
                        Moisture Percentage (%)
                        <input
                          type="number"
                          value={moisturePercentage}
                          onChange={(e) => setMoisturePercentage(e.target.value)}
                          min={5}
                          max={40}
                          step={0.1}
                        />
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Post-Harvest Losses (kg, optional)
                        <input
                          type="number"
                          placeholder="e.g. 50"
                          value={lossesKg}
                          onChange={(e) => setLossesKg(e.target.value)}
                          min={0}
                          step={1}
                        />
                      </label>

                      <label>
                        Notes / Mill Receipt / Storage Location
                        <input
                          type="text"
                          placeholder="e.g. Stored at Farm Barn #2 or Paddy Marketing Board receipt #881"
                          value={harvestNotes}
                          onChange={(e) => setHarvestNotes(e.target.value)}
                          maxLength={500}
                        />
                      </label>
                    </div>

                    <label className="checkbox-row" style={{ marginTop: '4px' }}>
                      <input
                        type="checkbox"
                        checked={addToStock}
                        onChange={(e) => setAddToStock(e.target.checked)}
                      />
                      <span>Add harvested paddy immediately to my Available Stored Stock</span>
                    </label>

                    {selectedHarvestCultivation && (
                      <div className="cultivation-preview-strip">
                        <div className="preview-item">
                          <span>Field & Area</span>
                          <strong>{selectedHarvestCultivation.fieldName} ({selectedHarvestCultivation.areaHectares.toFixed(2)} ha)</strong>
                        </div>
                        <div className="preview-item">
                          <span>Variety & Cycle</span>
                          <strong>{selectedHarvestCultivation.riceVarietyName} ({selectedHarvestCultivation.varietyCode})</strong>
                        </div>
                        <div className="preview-item highlight">
                          <span>Growth Stage Advance</span>
                          <strong>Will advance to 'Harvested'</strong>
                        </div>
                      </div>
                    )}

                    <button
                      type="submit"
                      className="extension-primary-button"
                      disabled={busy || !harvestCultivationId || !actualPaddyKg}
                    >
                      {busy ? 'Recording Harvest…' : 'Log Harvest & Update Stock'}
                    </button>
                  </form>
                )}

                {/* Farmer Stored Paddy Inventory Overview */}
                <div className="cultivation-my-cycles">
                  <div className="section-header-compact">
                    <div>
                      <h3>Stored Paddy Stock Inventory</h3>
                      <p className="form-subtext">Post-harvest paddy available for mill processing or commercial buyer sale.</p>
                    </div>
                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                      <span className="record-count" style={{ background: '#dcfce7', color: '#166534', fontWeight: 700 }}>
                        {(totalStockKg / 1000).toFixed(2)} MT Total
                      </span>
                      <button
                        type="button"
                        className="extension-secondary-button"
                        style={{ height: '32px', padding: '0 10px' }}
                        onClick={() => setShowStockForm(!showStockForm)}
                      >
                        {showStockForm ? 'Close Entry' : '+ Manual Stock Entry'}
                      </button>
                    </div>
                  </div>

                  {/* Manual Stock Entry Form */}
                  {showStockForm && (
                    <form className="stock-inline-form extension-form" onSubmit={(e) => void handleUpsertStock(e)}>
                      <h4>Record Stored Paddy / Rice Stock</h4>
                      <div className="form-row-2col">
                        <label>
                          Rice Variety
                          <select
                            value={stockVarietyId}
                            onChange={(e) => setStockVarietyId(e.target.value)}
                            required
                          >
                            {varieties.map((v) => (
                              <option key={v.id} value={v.id}>
                                {v.name} ({v.varietyCode})
                              </option>
                            ))}
                          </select>
                        </label>

                        <label>
                          Storage District / Location
                          <input
                            type="text"
                            placeholder="e.g. Polonnaruwa"
                            value={stockLocation}
                            onChange={(e) => setStockLocation(e.target.value)}
                            required
                          />
                        </label>
                      </div>

                      <div className="form-row-2col">
                        <label>
                          Quantity (kg)
                          <input
                            type="number"
                            placeholder="e.g. 5000"
                            value={stockQuantityKg}
                            onChange={(e) => setStockQuantityKg(e.target.value)}
                            min={1}
                            required
                          />
                        </label>

                        <label>
                          Commodity Stage
                          <select
                            value={stockStage}
                            onChange={(e) => setStockStage(e.target.value)}
                          >
                            <option value="Paddy">Raw Paddy</option>
                            <option value="MilledRice">Milled Rice</option>
                          </select>
                        </label>
                      </div>

                      <div className="form-row-2col">
                        <label>
                          Quality Grade
                          <select
                            value={stockGrade}
                            onChange={(e) => setStockGrade(e.target.value)}
                          >
                            <option value="Grade A">Grade A</option>
                            <option value="Grade B">Grade B</option>
                            <option value="Standard">Standard</option>
                          </select>
                        </label>

                        <label>
                          As Of Date
                          <input
                            type="date"
                            value={stockAsOfDate}
                            onChange={(e) => setStockAsOfDate(e.target.value)}
                            required
                          />
                        </label>
                      </div>

                      <button type="submit" className="extension-primary-button" disabled={busy}>
                        {busy ? 'Saving…' : 'Save Stock Record'}
                      </button>
                    </form>
                  )}

                  {stocks.length === 0 ? (
                    <p className="extension-empty">No stored paddy stock recorded yet. Log a harvest to automatically credit inventory.</p>
                  ) : (
                    <div className="stock-summary-grid">
                      {stocks.map((st) => (
                        <div className="stock-card" key={st.id}>
                          <div className="stock-card-head">
                            <strong className="table-primary">{st.riceVarietyName}</strong>
                            <span className="code-chip">{st.commodityStage}</span>
                          </div>
                          <div className="stock-card-quantity">
                            <strong>{st.quantityKg.toLocaleString()}</strong>
                            <small>kg ({(st.quantityKg / 1000).toFixed(2)} MT)</small>
                          </div>
                          <div className="stock-card-meta">
                            <span>Location: <strong>{st.location}</strong> · Grade: <strong>{st.grade}</strong></span>
                            <span>As of: {formatDate(st.asOfDate)}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Farmer: My Harvest Logs Table with Forecast vs. Actual Variance & Accuracy */}
            {isFarmer && (
              <section className="cultivation-monitor-section" style={{ marginTop: '24px' }}>
                <div className="section-header-compact">
                  <div>
                    <span className="panel-eyebrow">YIELD VERIFICATION & FIDELITY</span>
                    <h3>My Recorded Harvests & Yield Accuracy</h3>
                    <p className="form-subtext">Direct comparison between intelligence engine yield forecasts and actual field yields.</p>
                  </div>
                  <span className="record-count">{harvests.length} harvest records</span>
                </div>

                {harvests.length === 0 ? (
                  <p className="extension-empty">No harvest records logged yet.</p>
                ) : (
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>FIELD & FARM</th>
                          <th>VARIETY & SEASON</th>
                          <th>HARVEST DATE</th>
                          <th>PROJECTED YIELD</th>
                          <th>ACTUAL HARVEST</th>
                          <th>VARIANCE</th>
                          <th>ACCURACY</th>
                          <th>QUALITY / MOISTURE</th>
                          <th>EVIDENCE / NOTES</th>
                        </tr>
                      </thead>
                      <tbody>
                        {harvests.map((item) => (
                          <tr key={item.id}>
                            <td>
                              <strong className="table-primary">{item.fieldName}</strong>
                              <span className="table-secondary">{item.farmName} · {item.areaHectares.toFixed(2)} ha</span>
                            </td>
                            <td>
                              <strong className="table-primary">{item.riceVarietyName}</strong>
                              <span className="table-secondary">{item.season} ({item.varietyCode})</span>
                            </td>
                            <td>{formatDate(item.harvestDate)}</td>
                            <td>
                              {item.forecastedYieldKg !== null
                                ? <strong>{item.forecastedYieldKg.toLocaleString()} kg</strong>
                                : '—'}
                            </td>
                            <td>
                              <strong style={{ color: '#166534', fontSize: '13px' }}>
                                {item.actualPaddyKg.toLocaleString()} kg
                              </strong>
                            </td>
                            <td>
                              {item.varianceKg !== null ? (
                                <span className={item.varianceKg >= 0 ? 'variance-positive' : 'variance-negative'}>
                                  {item.varianceKg >= 0 ? `+${item.varianceKg.toLocaleString()}` : item.varianceKg.toLocaleString()} kg
                                  {item.variancePercent !== null && ` (${item.variancePercent >= 0 ? '+' : ''}${item.variancePercent.toFixed(1)}%)`}
                                </span>
                              ) : '—'}
                            </td>
                            <td>
                              {item.accuracyPercent !== null ? (
                                <span className={`accuracy-badge ${getAccuracyClass(item.accuracyPercent)}`}>
                                  {item.accuracyPercent.toFixed(1)}%
                                </span>
                              ) : (
                                <span className="status-badge status-neutral">N/A</span>
                              )}
                            </td>
                            <td>{item.quality || 'Standard'}</td>
                            <td>{item.evidenceReference || '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            )}

            {/* Officer / Admin / Government: Regional Harvest Audits & Stock Holdings */}
            {(isAdminOrGov || isOfficer) && (
              <section className="cultivation-monitor-section">
                <div className="section-header-compact">
                  <div>
                    <span className="panel-eyebrow">REGIONAL HARVEST AUDIT</span>
                    <h3>Regional Harvest Yields & Model Accuracy</h3>
                    <p className="form-subtext">Ground-truth harvest yield logs and predictive model accuracy across agrarian districts.</p>
                  </div>
                  <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    {averageAccuracy !== null && (
                      <span className="accuracy-badge accuracy-high" style={{ fontSize: '13px', padding: '5px 12px' }}>
                        Avg Accuracy: {averageAccuracy.toFixed(1)}%
                      </span>
                    )}
                    <span className="record-count">{filteredHarvests.length} harvest logs</span>
                  </div>
                </div>

                {/* Filter strip */}
                <div className="cultivation-filter-strip">
                  <label>
                    Season
                    <select value={filterSeason} onChange={(e) => setFilterSeason(e.target.value)}>
                      <option value="">All Seasons</option>
                      {seasons.map((s) => (
                        <option key={s.id} value={s.name}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Province
                    <select
                      value={filterProvince}
                      onChange={(e) => {
                        setFilterProvince(e.target.value)
                        setFilterDistrict('')
                      }}
                    >
                      <option value="">All Provinces</option>
                      {SRI_LANKA_PROVINCES.map((prov) => (
                        <option key={prov} value={prov}>
                          {prov}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    District
                    <select
                      value={filterDistrict}
                      onChange={(e) => setFilterDistrict(e.target.value)}
                      disabled={!filterProvince && filterDistricts.length === 0}
                    >
                      <option value="">All Districts</option>
                      {filterDistricts.map((dist) => (
                        <option key={dist} value={dist}>
                          {dist}
                        </option>
                      ))}
                    </select>
                  </label>

                  {(filterSeason || filterProvince || filterDistrict) && (
                    <button
                      type="button"
                      className="extension-secondary-button"
                      onClick={() => {
                        setFilterSeason('')
                        setFilterProvince('')
                        setFilterDistrict('')
                      }}
                      style={{ alignSelf: 'flex-end', height: '36px' }}
                    >
                      Clear filters
                    </button>
                  )}
                </div>

                {filteredHarvests.length === 0 ? (
                  <p className="extension-empty" style={{ padding: '16px 0' }}>
                    No harvest records found matching filter criteria.
                  </p>
                ) : (
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>FARMER</th>
                          <th>FIELD & DISTRICT</th>
                          <th>VARIETY & SEASON</th>
                          <th>HARVEST DATE</th>
                          <th>PROJECTED</th>
                          <th>ACTUAL HARVEST</th>
                          <th>VARIANCE</th>
                          <th>MODEL ACCURACY</th>
                          <th>QUALITY / MOISTURE</th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredHarvests.map((item) => (
                          <tr key={item.id}>
                            <td>
                              <strong className="table-primary">{item.farmerName}</strong>
                            </td>
                            <td>
                              <strong className="table-primary">{item.fieldName}</strong>
                              <span className="table-secondary">{item.district || '—'} ({item.areaHectares.toFixed(2)} ha)</span>
                            </td>
                            <td>
                              <strong className="table-primary">{item.riceVarietyName}</strong>
                              <span className="table-secondary">{item.season}</span>
                            </td>
                            <td>{formatDate(item.harvestDate)}</td>
                            <td>
                              {item.forecastedYieldKg !== null
                                ? `${item.forecastedYieldKg.toLocaleString()} kg`
                                : '—'}
                            </td>
                            <td>
                              <strong style={{ color: '#166534' }}>
                                {item.actualPaddyKg.toLocaleString()} kg
                              </strong>
                            </td>
                            <td>
                              {item.varianceKg !== null ? (
                                <span className={item.varianceKg >= 0 ? 'variance-positive' : 'variance-negative'}>
                                  {item.varianceKg >= 0 ? `+${item.varianceKg.toLocaleString()}` : item.varianceKg.toLocaleString()} kg
                                  {item.variancePercent !== null && ` (${item.variancePercent >= 0 ? '+' : ''}${item.variancePercent.toFixed(1)}%)`}
                                </span>
                              ) : '—'}
                            </td>
                            <td>
                              {item.accuracyPercent !== null ? (
                                <span className={`accuracy-badge ${getAccuracyClass(item.accuracyPercent)}`}>
                                  {item.accuracyPercent.toFixed(1)}%
                                </span>
                              ) : (
                                <span className="status-badge status-neutral">N/A</span>
                              )}
                            </td>
                            <td>{item.quality || 'Standard'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}

                {/* Stored Paddy Stock Holdings Section */}
                <div className="section-header-compact" style={{ marginTop: '28px' }}>
                  <div>
                    <span className="panel-eyebrow">POST-HARVEST INVENTORY</span>
                    <h3>Recorded Paddy Stock Holdings</h3>
                    <p className="form-subtext">Aggregated and individual post-harvest stock positions across storage centers.</p>
                  </div>
                  <div style={{ display: 'flex', gap: '8px' }}>
                    <span className="record-count">
                      {(stocks.reduce((acc, s) => acc + s.quantityKg, 0) / 1000).toFixed(2)} MT Total Stock
                    </span>
                    <button
                      type="button"
                      className="extension-secondary-button"
                      style={{ height: '32px', padding: '0 10px' }}
                      onClick={() => setShowStockForm(!showStockForm)}
                    >
                      {showStockForm ? 'Close Entry' : '+ Record Stock'}
                    </button>
                  </div>
                </div>

                {showStockForm && (
                  <form className="stock-inline-form extension-form" onSubmit={(e) => void handleUpsertStock(e)}>
                    <h4>Record Regional Stored Paddy / Rice Stock</h4>
                    <div className="form-row-2col">
                      <label>
                        Rice Variety
                        <select
                          value={stockVarietyId}
                          onChange={(e) => setStockVarietyId(e.target.value)}
                          required
                        >
                          {varieties.map((v) => (
                            <option key={v.id} value={v.id}>
                              {v.name} ({v.varietyCode})
                            </option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Storage District / Location
                        <input
                          type="text"
                          placeholder="e.g. Polonnaruwa"
                          value={stockLocation}
                          onChange={(e) => setStockLocation(e.target.value)}
                          required
                        />
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Quantity (kg)
                        <input
                          type="number"
                          placeholder="e.g. 15000"
                          value={stockQuantityKg}
                          onChange={(e) => setStockQuantityKg(e.target.value)}
                          min={1}
                          required
                        />
                      </label>

                      <label>
                        Commodity Stage
                        <select
                          value={stockStage}
                          onChange={(e) => setStockStage(e.target.value)}
                        >
                          <option value="Paddy">Raw Paddy</option>
                          <option value="MilledRice">Milled Rice</option>
                        </select>
                      </label>
                    </div>

                    <div className="form-row-2col">
                      <label>
                        Quality Grade
                        <select
                          value={stockGrade}
                          onChange={(e) => setStockGrade(e.target.value)}
                        >
                          <option value="Grade A">Grade A</option>
                          <option value="Grade B">Grade B</option>
                          <option value="Standard">Standard</option>
                        </select>
                      </label>

                      <label>
                        As Of Date
                        <input
                          type="date"
                          value={stockAsOfDate}
                          onChange={(e) => setStockAsOfDate(e.target.value)}
                          required
                        />
                      </label>
                    </div>

                    <button type="submit" className="extension-primary-button" disabled={busy}>
                      {busy ? 'Saving…' : 'Save Stock Record'}
                    </button>
                  </form>
                )}

                {stocks.length === 0 ? (
                  <p className="extension-empty" style={{ padding: '16px 0' }}>No stock holdings recorded yet.</p>
                ) : (
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>LOCATION</th>
                          <th>HOLDER / FARMER</th>
                          <th>VARIETY</th>
                          <th>STAGE</th>
                          <th>GRADE</th>
                          <th>QUANTITY</th>
                          <th>AS OF DATE</th>
                        </tr>
                      </thead>
                      <tbody>
                        {stocks.map((st) => (
                          <tr key={st.id}>
                            <td><strong className="table-primary">{st.location}</strong></td>
                            <td>{st.farmerName || 'Government / PMB'}</td>
                            <td>
                              <strong className="table-primary">{st.riceVarietyName}</strong>
                              <span className="table-secondary">{st.varietyCode}</span>
                            </td>
                            <td>{st.commodityStage}</td>
                            <td>{st.grade}</td>
                            <td>
                              <strong style={{ color: '#166534' }}>
                                {st.quantityKg.toLocaleString()} kg ({(st.quantityKg / 1000).toFixed(2)} MT)
                              </strong>
                            </td>
                            <td>{formatDate(st.asOfDate)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
