import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

type FieldItem = {
  id: string
  name: string
  farmName: string
  areaHectares: number
  boundaryGeoJson: string | null
  latitude: number | null
  longitude: number | null
  district: string | null
  province: string | null
  ownerName: string
  verificationStatus: string
}

type RegionSummary = {
  district: string
  totalFields: number
  verifiedFields: number
  verifiedAreaHectares: number
}

type FieldMapResponse = {
  visibility: 'fields' | 'aggregate'
  fields: FieldItem[]
  regions: RegionSummary[]
}

type Farm = {
  id: string
  name: string
  areaHectares: number
  district: string | null
  province: string | null
}

type VerificationItem = {
  id: string
  fieldId: string
  fieldName: string
  farmName: string
  ownerName: string
  areaHectares: number
  boundaryGeoJson: string | null
  district: string | null
  reviewNotes: string | null
}

type DrawPoint = [number, number]

function createBoundaryGeoJson(points: DrawPoint[]) {
  if (points.length < 3) return ''

  const ring = [...points, points[0]].map(([latitude, longitude]) => [longitude, latitude])
  return JSON.stringify({ type: 'Polygon', coordinates: [ring] }, null, 2)
}

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, credentials: 'include' })
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string; detail?: string } | null
    throw new Error(body?.error || body?.detail || `Request failed (${response.status}).`)
  }
  return response.json() as Promise<T>
}

function estimateHectares(points: DrawPoint[]) {
  if (points.length < 3) return 0
  const closed = [...points, points[0]]
  const radius = 6_371_008.8
  let total = 0
  for (let index = 0; index < closed.length - 1; index += 1) {
    const current = closed[index]
    const next = closed[index + 1]
    total += (next[1] - current[1]) * Math.PI / 180 *
      (2 + Math.sin(current[0] * Math.PI / 180) + Math.sin(next[0] * Math.PI / 180))
  }
  return Math.round(Math.abs(total) * radius * radius / 20_000) / 10_000
}

export default function FieldMapPage({ role }: { role: string }) {
  const mapElement = useRef<HTMLDivElement | null>(null)
  const map = useRef<L.Map | null>(null)
  const fieldsLayer = useRef<L.LayerGroup | null>(null)
  const drawingLayer = useRef<L.LayerGroup | null>(null)
  const drawMode = useRef(false)
  const currentPoints = useRef<DrawPoint[]>([])
  const [data, setData] = useState<FieldMapResponse | null>(null)
  const [farms, setFarms] = useState<Farm[]>([])
  const [queue, setQueue] = useState<VerificationItem[]>([])
  const [selectedField, setSelectedField] = useState<FieldItem | null>(null)
  const [drawPoints, setDrawPoints] = useState<DrawPoint[]>([])
  const [isDrawing, setIsDrawing] = useState(false)
  const [farmId, setFarmId] = useState('')
  const [farmName, setFarmName] = useState('')
  const [farmArea, setFarmArea] = useState('')
  const [district, setDistrict] = useState('')
  const [province, setProvince] = useState('')
  const [fieldName, setFieldName] = useState('')
  const [reviewNotes, setReviewNotes] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const canCreate = role === 'Farmer'
  const canReview = role === 'Officer' || role === 'Government'
  const boundaryGeoJson = createBoundaryGeoJson(drawPoints)

  const loadData = useCallback(async () => {
    const [mapData, farmData, queueData] = await Promise.all([
      requestJson<FieldMapResponse>('/api/field-map'),
      canCreate ? requestJson<Farm[]>('/api/field-map/farms') : Promise.resolve([] as Farm[]),
      canReview ? requestJson<VerificationItem[]>('/api/field-map/verification-queue') : Promise.resolve([] as VerificationItem[]),
    ])
    return { mapData, farmData, queueData }
  }, [canCreate, canReview])

  const applyLoadedData = useCallback((loaded: Awaited<ReturnType<typeof loadData>>) => {
    setData(loaded.mapData)
    if (canCreate) {
      setFarms(loaded.farmData)
      setFarmId((current) => current || loaded.farmData[0]?.id || '')
    }
    if (canReview) setQueue(loaded.queueData)
  }, [canCreate, canReview])

  useEffect(() => {
    let active = true
    void loadData()
      .then((loaded) => { if (active) applyLoadedData(loaded) })
      .catch((requestError: unknown) => {
        if (active) setError(requestError instanceof Error ? requestError.message : 'Unable to load field map data.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [applyLoadedData, loadData])

  const refreshData = () => {
    setLoading(true)
    setError('')
    void loadData()
      .then(applyLoadedData)
      .catch((requestError: unknown) => setError(requestError instanceof Error ? requestError.message : 'Unable to load field map data.'))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    if (!mapElement.current || map.current) return

    const mapInstance = L.map(mapElement.current).setView([7.9, 80.7], 7)
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(mapInstance)

    const fieldGroup = L.layerGroup().addTo(mapInstance)
    const drawingGroup = L.layerGroup().addTo(mapInstance)
    map.current = mapInstance
    fieldsLayer.current = fieldGroup
    drawingLayer.current = drawingGroup

    mapInstance.on('click', (event: L.LeafletMouseEvent) => {
      if (!drawMode.current) return
      const nextPoints = [...currentPoints.current, [event.latlng.lat, event.latlng.lng] as DrawPoint]
      currentPoints.current = nextPoints
      setDrawPoints(nextPoints)
    })

    return () => {
      mapInstance.remove()
      map.current = null
      fieldsLayer.current = null
      drawingLayer.current = null
    }
  }, [])

  useEffect(() => {
    const layer = fieldsLayer.current
    if (!layer || !data || data.visibility !== 'fields') return
    layer.clearLayers()

    data.fields.forEach((field) => {
      const style = {
        color: field.verificationStatus === 'Verified' ? '#3d8659' : '#c48b37',
        fillColor: field.verificationStatus === 'Verified' ? '#7cb28a' : '#e5bc72',
        fillOpacity: 0.35,
        weight: 2,
      }
      if (field.boundaryGeoJson) {
        try {
          const geometry = JSON.parse(field.boundaryGeoJson)
          L.geoJSON(geometry, {
            style,
            onEachFeature: (_feature, featureLayer) => {
              featureLayer.on('click', () => setSelectedField(field))
              featureLayer.bindTooltip(`${field.name} · ${field.verificationStatus}`)
            },
          }).addTo(layer)
        } catch {
          if (field.latitude !== null && field.longitude !== null) {
            L.circleMarker([field.latitude, field.longitude], { ...style, radius: 7 })
              .addTo(layer)
              .on('click', () => setSelectedField(field))
          }
        }
      } else if (field.latitude !== null && field.longitude !== null) {
        L.circleMarker([field.latitude, field.longitude], { ...style, radius: 7 })
          .addTo(layer)
          .on('click', () => setSelectedField(field))
      }
    })
  }, [data])

  useEffect(() => {
    const layer = drawingLayer.current
    if (!layer) return
    layer.clearLayers()

    drawPoints.forEach(([latitude, longitude]) => {
      L.circleMarker([latitude, longitude], {
        radius: 5,
        color: '#183c30',
        fillColor: '#d3ec9a',
        fillOpacity: 1,
      }).addTo(layer)
    })
    if (drawPoints.length >= 3) {
      L.polygon(drawPoints, {
        color: '#236443',
        fillColor: '#cde79a',
        fillOpacity: 0.3,
        dashArray: '6 5',
        weight: 2,
      }).addTo(layer)
    } else if (drawPoints.length > 1) {
      L.polyline(drawPoints, { color: '#183c30', dashArray: '6 5', weight: 2 }).addTo(layer)
    }
  }, [drawPoints])

  const startDrawing = () => {
    drawMode.current = true
    setIsDrawing(true)
    currentPoints.current = []
    setDrawPoints([])
    setNotice('Click the map to mark the paddy field boundary. Use at least three points.')
  }

  const finishDrawing = () => {
    drawMode.current = false
    setIsDrawing(false)
    setNotice('Boundary marking finished. Review or copy the GeoJSON coordinates.')
  }

  const clearDrawing = () => {
    drawMode.current = false
    setIsDrawing(false)
    currentPoints.current = []
    setDrawPoints([])
    setNotice('')
  }

  const copyBoundaryCoordinates = async () => {
    if (!boundaryGeoJson) return
    try {
      await navigator.clipboard.writeText(boundaryGeoJson)
      setError('')
      setNotice('GeoJSON coordinates copied to the clipboard.')
    } catch {
      setError('Clipboard access was denied. Select and copy the coordinates from the text box.')
    }
  }

  const createFarm = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const farm = await requestJson<Farm>('/api/field-map/farms', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: farmName,
          areaHectares: Number(farmArea),
          district,
          province,
        }),
      })
      setFarmName('')
      setFarmArea('')
      setFarms((current) => [...current, farm])
      setFarmId(farm.id)
      setNotice('Farm registered.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to register farm.')
    } finally {
      setBusy(false)
    }
  }

  const createField = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (drawPoints.length < 3) {
      setError('Mark a field boundary with at least three points first.')
      return
    }
    setBusy(true)
    drawMode.current = false
    setIsDrawing(false)
    setError('')
    setNotice('')
    try {
      const result = await requestJson<{ areaHectares: number; possibleOverlap: boolean }>('/api/field-map/fields', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          farmId,
          name: fieldName,
          boundaryGeoJson,
        }),
      })
      setFieldName('')
      clearDrawing()
      setNotice(result.possibleOverlap
        ? `Field submitted for verification. The boundary has a possible overlap; server-calculated area is ${result.areaHectares.toFixed(2)} ha.`
        : `Field submitted for verification. Server-calculated area is ${result.areaHectares.toFixed(2)} ha.`)
      applyLoadedData(await loadData())
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to submit field.')
    } finally {
      setBusy(false)
    }
  }

  const reviewField = async (item: VerificationItem, status: 'Verified' | 'Returned' | 'Rejected') => {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestJson(`/api/field-map/fields/${item.fieldId}/verification`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status, notes: reviewNotes[item.fieldId] || '' }),
      })
      setNotice(`${item.fieldName} marked ${status.toLowerCase()}.`)
      applyLoadedData(await loadData())
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to update verification.')
    } finally {
      setBusy(false)
    }
  }

  const fitSriLanka = () => map.current?.setView([7.9, 80.7], 7)

  return (
    <div className="extension-page">
      <div className="extension-card field-map-card">
        <div className="extension-toolbar">
          <div>
            <span className="panel-eyebrow">FARMS & FIELDS</span>
            <h2>Field map</h2>
            <p>Boundaries are measured from GeoJSON and reviewed before they count as verified supply.</p>
          </div>
          <button type="button" className="extension-secondary-button" onClick={refreshData} disabled={loading}>
            {loading ? 'Refreshing…' : 'Refresh map'}
          </button>
        </div>

        {error && <p className="extension-alert extension-alert-error" role="alert">{error}</p>}
        {notice && <p className="extension-alert extension-alert-success" role="status">{notice}</p>}

        <div className={`field-entry-grid ${canCreate ? '' : 'field-entry-grid-viewer'}`}>
            {canCreate && farms.length === 0 && (
              <form className="extension-form farm-create-form" onSubmit={(event) => void createFarm(event)}>
                <h3>Register a farm</h3>
                <label>Farm name<input value={farmName} onChange={(event) => setFarmName(event.target.value)} required maxLength={120} /></label>
                <label>Farm area (ha)<input type="number" min="0.01" step="0.01" value={farmArea} onChange={(event) => setFarmArea(event.target.value)} required /></label>
                <label>District<input value={district} onChange={(event) => setDistrict(event.target.value)} maxLength={120} /></label>
                <label>Province<input value={province} onChange={(event) => setProvince(event.target.value)} maxLength={120} /></label>
                <button className="extension-primary-button" disabled={busy}>Save farm</button>
              </form>
            )}
            {canCreate && farms.length > 0 && (
              <form className="extension-form field-create-form" onSubmit={(event) => void createField(event)}>
                <h3>Add a field boundary</h3>
                <div className="field-form-row">
                  <label>Farm
                    <select value={farmId} onChange={(event) => setFarmId(event.target.value)} required>
                      {farms.map((farm) => <option key={farm.id} value={farm.id}>{farm.name}</option>)}
                    </select>
                  </label>
                  <label>Field name<input value={fieldName} onChange={(event) => setFieldName(event.target.value)} required maxLength={120} /></label>
                </div>
                <button className="extension-primary-button" disabled={busy || drawPoints.length < 3}>Submit for verification</button>
              </form>
            )}
            <section className="extension-form field-drawing-form" aria-labelledby="field-drawing-title">
              <h3 id="field-drawing-title">Mark a paddy field area</h3>
              <p>Click the map to place boundary points. At least three points are needed to create a polygon.</p>
              <div className="field-drawing-actions">
                <button type="button" className="extension-secondary-button" onClick={isDrawing ? finishDrawing : startDrawing}>
                  {isDrawing ? 'Finish marking' : 'Mark field area'}
                </button>
                <button type="button" className="extension-secondary-button" onClick={clearDrawing} disabled={drawPoints.length === 0}>Clear points</button>
                <span>{drawPoints.length} points · {estimateHectares(drawPoints).toFixed(2)} ha</span>
              </div>
              <div className="field-coordinate-export">
                <label htmlFor="field-boundary-geojson">Boundary coordinates (GeoJSON Polygon)</label>
                <textarea
                  id="field-boundary-geojson"
                  value={boundaryGeoJson}
                  readOnly
                  rows={6}
                  placeholder="Mark at least three points on the map to generate coordinates."
                />
                <small>Coordinates use [longitude, latitude] order and close back at the first point.</small>
                <button type="button" className="extension-secondary-button" onClick={() => void copyBoundaryCoordinates()} disabled={!boundaryGeoJson}>
                  Copy coordinates
                </button>
              </div>
            </section>
        </div>

        <div className="field-map-layout">
          <div className="field-map-frame">
            <div className="field-map-commands">
              <span>OpenStreetMap · click a boundary to inspect it</span>
              <button type="button" onClick={fitSriLanka}>Show Sri Lanka</button>
            </div>
            <div className="field-map-canvas" ref={mapElement} aria-label="Map of paddy fields" />
          </div>
          <aside className="field-map-sidebar">
            {data?.visibility === 'aggregate' ? (
              <>
                <h3>Regional field summary</h3>
                <p className="field-map-privacy">Field boundaries and member identities are not shown to Buyers.</p>
                {data.regions.map((region) => (
                  <div className="field-summary-card" key={region.district}>
                    <strong>{region.district}</strong>
                    <span>{region.verifiedFields} of {region.totalFields} fields verified</span>
                    <small>{region.verifiedAreaHectares.toFixed(2)} verified ha</small>
                  </div>
                ))}
                {!data.regions.length && <p className="extension-empty">No regional field records are available.</p>}
              </>
            ) : (
              <>
                <h3>{selectedField ? 'Field details' : 'Mapped fields'}</h3>
                {selectedField ? (
                  <div className="field-selected-card">
                    <button type="button" className="field-back-button" onClick={() => setSelectedField(null)}>← All fields</button>
                    <strong>{selectedField.name}</strong>
                    <span>{selectedField.farmName}</span>
                    <span>{selectedField.district || 'District not set'} · {selectedField.areaHectares.toFixed(2)} ha</span>
                    <span className={`field-status field-status-${selectedField.verificationStatus.toLowerCase()}`}>
                      {selectedField.verificationStatus}
                    </span>
                    {role !== 'Farmer' && <small>Owner: {selectedField.ownerName}</small>}
                  </div>
                ) : data?.fields.map((field) => (
                  <button type="button" className="field-summary-card field-summary-button" key={field.id} onClick={() => {
                    setSelectedField(field)
                    if (field.latitude !== null && field.longitude !== null) {
                      map.current?.setView([field.latitude, field.longitude], 15)
                    }
                  }}>
                    <strong>{field.name}</strong>
                    <span>{field.farmName} · {field.district || 'District not set'}</span>
                    <small>{field.areaHectares.toFixed(2)} ha · {field.verificationStatus}</small>
                  </button>
                ))}
                {!loading && !data?.fields.length && <p className="extension-empty">No field boundaries yet.</p>}
              </>
            )}
          </aside>
        </div>
      </div>

      {canReview && (
        <section className="extension-card verification-queue">
          <div className="extension-toolbar">
            <div><span className="panel-eyebrow">AGRARIAN REVIEW</span><h2>Field verification queue</h2></div>
            <span className="record-count">{queue.length} pending</span>
          </div>
          {queue.map((item) => (
            <article className="verification-row" key={item.id}>
              <div>
                <strong>{item.fieldName} <span>· {item.farmName}</span></strong>
                <small>{item.ownerName} · {item.district || 'District not set'} · {item.areaHectares.toFixed(2)} ha</small>
                {item.reviewNotes && <p>{item.reviewNotes}</p>}
              </div>
              <div className="verification-actions">
                <input
                  aria-label={`Review note for ${item.fieldName}`}
                  placeholder="Reason required for return/reject"
                  value={reviewNotes[item.fieldId] || ''}
                  onChange={(event) => setReviewNotes((current) => ({ ...current, [item.fieldId]: event.target.value }))}
                />
                <button type="button" className="extension-primary-button" disabled={busy} onClick={() => void reviewField(item, 'Verified')}>Verify</button>
                <button type="button" className="extension-secondary-button" disabled={busy} onClick={() => void reviewField(item, 'Returned')}>Return</button>
                <button type="button" className="extension-danger-button" disabled={busy} onClick={() => void reviewField(item, 'Rejected')}>Reject</button>
              </div>
            </article>
          ))}
          {!queue.length && <p className="extension-empty">No fields are waiting for review.</p>}
        </section>
      )}
    </div>
  )
}
