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

export default function CultivationPage({ role }: { role: string }) {
  const [seasons, setSeasons] = useState<SeasonItem[]>([])
  const [varieties, setVarieties] = useState<RiceVarietyItem[]>([])
  const [myFields, setMyFields] = useState<FieldMapItem[]>([])
  const [cultivations, setCultivations] = useState<CultivationItem[]>([])

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

  // Filter states for Admin/Officer Cultivation Monitor
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

  const loadData = useCallback(async () => {
    const promises: [
      Promise<SeasonItem[]>,
      Promise<RiceVarietyItem[]>,
      Promise<CultivationItem[]>,
      Promise<{ fields: FieldMapItem[] }>
    ] = [
      requestJson<SeasonItem[]>('/api/seasons').catch(() => []),
      requestJson<RiceVarietyItem[]>('/api/rice-varieties').catch(() => []),
      requestJson<CultivationItem[]>('/api/cultivations').catch(() => []),
      isFarmer
        ? requestJson<{ fields: FieldMapItem[] }>('/api/field-map').catch(() => ({ fields: [] }))
        : Promise.resolve({ fields: [] }),
    ]

    const [seasonList, varietyList, cultivationList, fieldData] = await Promise.all(promises)
    return { seasonList, varietyList, cultivationList, fields: fieldData.fields || [] }
  }, [isFarmer])

  const applyData = useCallback(
    (loaded: Awaited<ReturnType<typeof loadData>>) => {
      setSeasons(loaded.seasonList)
      setVarieties(loaded.varietyList)
      setCultivations(loaded.cultivationList)
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
    },
    [isFarmer, fieldId, seasonId, varietyId]
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
      // Refresh list
      const loaded = await loadData()
      applyData(loaded)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to log cultivation.')
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

  return (
    <div className="extension-page cultivation-page">
      <div className="extension-card cultivation-hero-card">
        <div className="extension-toolbar">
          <div>
            <span className="panel-eyebrow">CULTIVATION CYCLES & SEASONS</span>
            <h2>{isFarmer ? 'Crop Planning & Growth Tracking' : 'Cultivation & Seasonal Monitoring'}</h2>
            <p>
              Connect verified GPS field boundaries and rice varieties to official cropping seasons with real-time growth stage tracking.
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
      </div>
    </div>
  )
}
