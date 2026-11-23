import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import {
  SRI_LANKA_PROVINCES,
  getDistrictsForProvince,
  getProvinceForDistrict,
} from './sriLankaLocations'

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
type MapLayerMode = 'satellite' | 'hybrid' | 'streets'

function createBoundaryGeoJson(points: DrawPoint[]) {
  if (points.length < 3) return ''
  const ring = [...points, points[0]].map(([latitude, longitude]) => [longitude, latitude])
  return JSON.stringify({ type: 'Polygon', coordinates: [ring] }, null, 2)
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

/**
 * Computes geodesic polygon metrics on the WGS-84 ellipsoid/spherical model:
 * Hectares, Acres, Perches, and Perimeter in meters.
 */
export function computeGeodesicMetrics(points: DrawPoint[]) {
  if (points.length < 3) {
    return {
      hectares: 0,
      acres: 0,
      perches: 0,
      perimeterMeters: 0,
    }
  }

  const radius = 6378137.0 // WGS84 major radius
  let areaExcess = 0
  let perimeter = 0

  for (let i = 0; i < points.length; i++) {
    const p1 = points[i]
    const p2 = points[(i + 1) % points.length]

    // Haversine distance for perimeter
    const lat1Rad = (p1[0] * Math.PI) / 180
    const lat2Rad = (p2[0] * Math.PI) / 180
    const dLat = ((p2[0] - p1[0]) * Math.PI) / 180
    const dLon = ((p2[1] - p1[1]) * Math.PI) / 180

    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1Rad) * Math.cos(lat2Rad) * Math.sin(dLon / 2) * Math.sin(dLon / 2)
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
    perimeter += radius * c

    // Spherical excess for area
    const lon1Rad = (p1[1] * Math.PI) / 180
    const lon2Rad = (p2[1] * Math.PI) / 180
    areaExcess += (lon2Rad - lon1Rad) * (2 + Math.sin(lat1Rad) + Math.sin(lat2Rad))
  }

  const areaSqMeters = Math.abs((areaExcess * radius * radius) / 2.0)
  const hectares = areaSqMeters / 10000.0
  const acres = hectares * 2.4710538
  const perches = acres * 160.0

  return {
    hectares: Math.round(hectares * 10000) / 10000,
    acres: Math.round(acres * 100) / 100,
    perches: Math.round(perches * 10) / 10,
    perimeterMeters: Math.round(perimeter * 10) / 10,
  }
}

export default function FieldMapPage({ role }: { role: string }) {
  const mapElement = useRef<HTMLDivElement | null>(null)
  const map = useRef<L.Map | null>(null)
  const baseLayersRef = useRef<{
    satellite: L.TileLayer
    hybrid: { satellite: L.TileLayer; labels: L.TileLayer }
    streets: L.TileLayer
  } | null>(null)
  const fieldsLayer = useRef<L.LayerGroup | null>(null)
  const drawingLayer = useRef<L.LayerGroup | null>(null)
  const drawMode = useRef(false)
  const currentPoints = useRef<DrawPoint[]>([])

  const [activeLayer, setActiveLayer] = useState<MapLayerMode>('satellite')
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
  const boundaryGeoJson = useMemo(() => createBoundaryGeoJson(drawPoints), [drawPoints])
  const metrics = useMemo(() => computeGeodesicMetrics(drawPoints), [drawPoints])

  const availableDistricts = useMemo(() => getDistrictsForProvince(province), [province])

  const handleProvinceChange = (newProvince: string) => {
    setProvince(newProvince)
    const validDistricts = getDistrictsForProvince(newProvince)
    if (district && !validDistricts.includes(district)) {
      setDistrict('')
    }
  }

  const handleDistrictChange = (newDistrict: string) => {
    setDistrict(newDistrict)
    if (!province) {
      const matched = getProvinceForDistrict(newDistrict)
      if (matched) {
        setProvince(matched)
      }
    }
  }

  const loadData = useCallback(async () => {
    const [mapData, farmData, queueData] = await Promise.all([
      requestJson<FieldMapResponse>('/api/field-map'),
      canCreate ? requestJson<Farm[]>('/api/field-map/farms') : Promise.resolve([] as Farm[]),
      canReview
        ? requestJson<VerificationItem[]>('/api/field-map/verification-queue')
        : Promise.resolve([] as VerificationItem[]),
    ])
    return { mapData, farmData, queueData }
  }, [canCreate, canReview])

  const applyLoadedData = useCallback(
    (loaded: Awaited<ReturnType<typeof loadData>>) => {
      setData(loaded.mapData)
      if (canCreate) {
        setFarms(loaded.farmData)
        setFarmId((current) => current || loaded.farmData[0]?.id || '')
      }
      if (canReview) setQueue(loaded.queueData)
    },
    [canCreate, canReview],
  )

  useEffect(() => {
    let active = true
    void loadData()
      .then((loaded) => {
        if (active) applyLoadedData(loaded)
      })
      .catch((requestError: unknown) => {
        if (active)
          setError(
            requestError instanceof Error ? requestError.message : 'Unable to load field map data.',
          )
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [applyLoadedData, loadData])

  const refreshData = () => {
    setLoading(true)
    setError('')
    void loadData()
      .then(applyLoadedData)
      .catch((requestError: unknown) =>
        setError(
          requestError instanceof Error ? requestError.message : 'Unable to load field map data.',
        ),
      )
      .finally(() => setLoading(false))
  }

  // Initialize Leaflet map with Satellite view as default
  useEffect(() => {
    if (!mapElement.current || map.current) return

    const mapInstance = L.map(mapElement.current).setView([7.8731, 80.7718], 8)

    // Esri World Imagery Satellite (Default)
    const satelliteLayer = L.tileLayer(
      'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
      {
        maxZoom: 19,
        attribution:
          'Tiles &copy; Esri &mdash; Source: Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community',
      },
    )

    // Esri Labels overlay for Hybrid view
    const labelsOverlay = L.tileLayer(
      'https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}',
      {
        maxZoom: 19,
        attribution: 'Labels &copy; Esri',
      },
    )

    // OpenStreetMap for Street view
    const streetsLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    })

    // Add default satellite layer
    satelliteLayer.addTo(mapInstance)

    baseLayersRef.current = {
      satellite: satelliteLayer,
      hybrid: { satellite: satelliteLayer, labels: labelsOverlay },
      streets: streetsLayer,
    }

    const fieldGroup = L.layerGroup().addTo(mapInstance)
    const drawingGroup = L.layerGroup().addTo(mapInstance)
    map.current = mapInstance
    fieldsLayer.current = fieldGroup
    drawingLayer.current = drawingGroup

    mapInstance.on('click', (event: L.LeafletMouseEvent) => {
      if (!drawMode.current) return
      const nextPoints = [
        ...currentPoints.current,
        [event.latlng.lat, event.latlng.lng] as DrawPoint,
      ]
      currentPoints.current = nextPoints
      setDrawPoints(nextPoints)
    })

    return () => {
      mapInstance.remove()
      map.current = null
      fieldsLayer.current = null
      drawingLayer.current = null
      baseLayersRef.current = null
    }
  }, [])

  // Switch Map Layer without affecting active polygon vertices
  const switchLayer = (newMode: MapLayerMode) => {
    if (!map.current || !baseLayersRef.current || newMode === activeLayer) return
    const currentMap = map.current
    const { satellite, hybrid, streets } = baseLayersRef.current

    // Remove existing layers
    currentMap.removeLayer(satellite)
    currentMap.removeLayer(hybrid.labels)
    currentMap.removeLayer(streets)

    if (newMode === 'satellite') {
      satellite.addTo(currentMap)
    } else if (newMode === 'hybrid') {
      satellite.addTo(currentMap)
      hybrid.labels.addTo(currentMap)
    } else if (newMode === 'streets') {
      streets.addTo(currentMap)
    }

    setActiveLayer(newMode)
  }

  // Render registered fields
  useEffect(() => {
    const layer = fieldsLayer.current
    if (!layer || !data || data.visibility !== 'fields') return
    layer.clearLayers()

    data.fields.forEach((field) => {
      const style = {
        color: field.verificationStatus === 'Verified' ? '#10b981' : '#f59e0b',
        fillColor: field.verificationStatus === 'Verified' ? '#34d399' : '#fbbf24',
        fillOpacity: 0.4,
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

  // Helper functions for vertex manipulation
  const updateVertexPosition = useCallback((index: number, lat: number, lng: number) => {
    const nextPoints = [...currentPoints.current]
    nextPoints[index] = [lat, lng]
    currentPoints.current = nextPoints
    setDrawPoints(nextPoints)
  }, [])

  const insertMidpointVertex = useCallback((index: number, lat: number, lng: number) => {
    const nextPoints = [...currentPoints.current]
    nextPoints.splice(index, 0, [lat, lng])
    currentPoints.current = nextPoints
    setDrawPoints(nextPoints)
    setNotice(`Vertex added. Total: ${nextPoints.length} vertices.`)
  }, [])

  const deleteVertex = useCallback((index: number) => {
    if (currentPoints.current.length <= 3) {
      setError('A polygon requires a minimum of 3 vertices.')
      return
    }
    const nextPoints = currentPoints.current.filter((_, i) => i !== index)
    currentPoints.current = nextPoints
    setDrawPoints(nextPoints)
    setNotice(`Vertex removed. Total: ${nextPoints.length} vertices.`)
  }, [])

  // Render interactive drawing vertices, midpoint handles, and polygon
  useEffect(() => {
    const layer = drawingLayer.current
    if (!layer) return
    layer.clearLayers()

    // 1. Render polygon outline & fill
    if (drawPoints.length >= 3) {
      L.polygon(drawPoints, {
        color: '#34d399',
        fillColor: '#10b981',
        fillOpacity: 0.35,
        dashArray: '6 6',
        weight: 2.5,
      }).addTo(layer)
    } else if (drawPoints.length > 1) {
      L.polyline(drawPoints, { color: '#34d399', dashArray: '6 6', weight: 2.5 }).addTo(layer)
    }

    // 2. Render draggable vertex handles
    drawPoints.forEach(([latitude, longitude], index) => {
      const vertexIcon = L.divIcon({
        className: 'map-vertex-handle',
        html: `<span class="vertex-dot">${index + 1}</span>`,
        iconSize: [20, 20],
        iconAnchor: [10, 10],
      })

      const marker = L.marker([latitude, longitude], {
        icon: vertexIcon,
        draggable: true,
        title: `Vertex ${index + 1}: Drag to adjust coordinate. Right-click to delete.`,
      })

      marker.on('drag', (event: L.LeafletEvent) => {
        const targetMarker = event.target as L.Marker
        const newPos = targetMarker.getLatLng()
        updateVertexPosition(index, newPos.lat, newPos.lng)
      })

      marker.on('contextmenu', (event: L.LeafletMouseEvent) => {
        L.DomEvent.stopPropagation(event)
        deleteVertex(index)
      })

      marker.addTo(layer)
    })

    // 3. Render midpoint '+' handles between adjacent vertices
    if (drawPoints.length >= 3) {
      for (let i = 0; i < drawPoints.length; i++) {
        const p1 = drawPoints[i]
        const p2 = drawPoints[(i + 1) % drawPoints.length]
        const midpoint: [number, number] = [(p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2]

        const midIcon = L.divIcon({
          className: 'map-midpoint-handle',
          html: `<span class="midpoint-plus">+</span>`,
          iconSize: [16, 16],
          iconAnchor: [8, 8],
        })

        const midMarker = L.marker(midpoint, {
          icon: midIcon,
          title: `Click to insert vertex between ${i + 1} and ${((i + 1) % drawPoints.length) + 1}`,
        })

        const insertIndex = i + 1
        midMarker.on('click', (event: L.LeafletMouseEvent) => {
          L.DomEvent.stopPropagation(event)
          insertMidpointVertex(insertIndex, midpoint[0], midpoint[1])
        })

        midMarker.addTo(layer)
      }
    }
  }, [drawPoints, updateVertexPosition, insertMidpointVertex, deleteVertex])

  const startDrawing = () => {
    drawMode.current = true
    setIsDrawing(true)
    currentPoints.current = []
    setDrawPoints([])
    setNotice(
      'Click the map to place boundary vertices (minimum 3 points). Vertices can be dragged or deleted, and new points added via "+".',
    )
  }

  const finishDrawing = () => {
    drawMode.current = false
    setIsDrawing(false)
    setNotice(
      'Boundary marking complete. You can drag vertices to refine, or click "+" midpoints to add precision.',
    )
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
      setNotice('GeoJSON coordinates copied to clipboard.')
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
      setNotice('Farm registered successfully.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to register farm.')
    } finally {
      setBusy(false)
    }
  }

  // Submit field with automatically calculated area (no manual size entry)
  const createField = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (drawPoints.length < 3) {
      setError('Mark a field boundary with at least three vertices first.')
      return
    }
    setBusy(true)
    drawMode.current = false
    setIsDrawing(false)
    setError('')
    setNotice('')
    try {
      const result = await requestJson<{ areaHectares: number; possibleOverlap: boolean }>(
        '/api/field-map/fields',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            farmId,
            name: fieldName,
            boundaryGeoJson,
            areaHectares: metrics.hectares, // Auto-calculated area submitted automatically
          }),
        },
      )
      setFieldName('')
      clearDrawing()
      setNotice(
        result.possibleOverlap
          ? `Field submitted for verification with possible overlap. Auto-measured area: ${result.areaHectares.toFixed(2)} ha.`
          : `Field submitted for verification. Auto-measured area: ${result.areaHectares.toFixed(2)} ha.`,
      )
      applyLoadedData(await loadData())
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to submit field.')
    } finally {
      setBusy(false)
    }
  }

  const reviewField = async (
    item: VerificationItem,
    status: 'Verified' | 'Returned' | 'Rejected',
  ) => {
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

  const fitSriLanka = () => map.current?.setView([7.8731, 80.7718], 8)

  return (
    <div className="extension-page">
      <div className="extension-card field-map-card">
        <div className="extension-toolbar">
          <div>
            <span className="panel-eyebrow">FARMS & FIELDS</span>
            <h2>Satellite paddy field map</h2>
            <p>
              High-resolution satellite imagery with automatic geodesic polygon area calculation.
            </p>
          </div>
          <button
            type="button"
            className="extension-secondary-button"
            onClick={refreshData}
            disabled={loading}
          >
            {loading ? 'Refreshing…' : 'Refresh map'}
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

        <div className={`field-entry-grid ${canCreate ? '' : 'field-entry-grid-viewer'}`}>
          {canCreate && farms.length === 0 && (
            <form
              className="extension-form farm-create-form"
              onSubmit={(event) => void createFarm(event)}
            >
              <h3>Register a farm</h3>
              <label>
                Farm name
                <input
                  value={farmName}
                  onChange={(event) => setFarmName(event.target.value)}
                  placeholder="e.g. Parakrama Plot A"
                  required
                  maxLength={120}
                />
              </label>
              <label>
                Total farm area (ha)
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={farmArea}
                  onChange={(event) => setFarmArea(event.target.value)}
                  placeholder="e.g. 5.5"
                  required
                />
              </label>
              <label>
                Province
                <input
                  list="fieldmap-farm-provinces"
                  value={province}
                  onChange={(event) => handleProvinceChange(event.target.value)}
                  placeholder="Select or enter province"
                  maxLength={120}
                />
                <datalist id="fieldmap-farm-provinces">
                  {SRI_LANKA_PROVINCES.map((p) => (
                    <option key={p} value={p} />
                  ))}
                </datalist>
              </label>
              <label>
                District
                <input
                  list="fieldmap-farm-districts"
                  value={district}
                  onChange={(event) => handleDistrictChange(event.target.value)}
                  placeholder="Select or enter district"
                  maxLength={120}
                />
                <datalist id="fieldmap-farm-districts">
                  {availableDistricts.map((d) => (
                    <option key={d} value={d} />
                  ))}
                </datalist>
              </label>
              <button className="extension-primary-button" disabled={busy}>
                Save farm
              </button>
            </form>
          )}

          {canCreate && farms.length > 0 && (
            <form
              className="extension-form field-create-form"
              onSubmit={(event) => void createField(event)}
            >
              <h3>Add a field boundary</h3>
              <div className="field-form-row">
                <label>
                  Farm
                  <select
                    value={farmId}
                    onChange={(event) => setFarmId(event.target.value)}
                    required
                  >
                    {farms.map((farm) => (
                      <option key={farm.id} value={farm.id}>
                        {farm.name} ({farm.areaHectares.toFixed(2)} ha)
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Field name
                  <input
                    value={fieldName}
                    onChange={(event) => setFieldName(event.target.value)}
                    placeholder="e.g. Northern Paddy Strip"
                    required
                    maxLength={120}
                  />
                </label>
              </div>

              {/* Automatic map-calculated field area readout (no manual entry) */}
              <div className="field-auto-area-card">
                <label>Auto-calculated field area</label>
                <div className="field-auto-area-value">
                  <strong>
                    {metrics.hectares > 0 ? `${metrics.hectares.toFixed(2)} ha` : '—'}
                  </strong>
                  {metrics.hectares > 0 && (
                    <span>
                      ({metrics.acres.toFixed(2)} acres / {metrics.perches.toFixed(1)} perches)
                    </span>
                  )}
                </div>
              </div>

              <button
                className="extension-primary-button"
                disabled={busy || drawPoints.length < 3}
              >
                Submit for verification
              </button>
            </form>
          )}

          <section
            className="extension-form field-drawing-form"
            aria-labelledby="field-drawing-title"
          >
            <h3 id="field-drawing-title">Interactive boundary drawing</h3>
            <p>
              Click map to add boundary points. Drag vertices to adjust boundaries, click "+"
              midpoints to insert points, or right-click a vertex to delete.
            </p>
            <div className="field-drawing-actions">
              <button
                type="button"
                className="extension-secondary-button"
                onClick={isDrawing ? finishDrawing : startDrawing}
              >
                {isDrawing ? 'Finish marking' : 'Mark field area'}
              </button>
              <button
                type="button"
                className="extension-secondary-button"
                onClick={clearDrawing}
                disabled={drawPoints.length === 0}
              >
                Clear points
              </button>
              <span>{drawPoints.length} vertices placed</span>
            </div>

            {/* Read-only live telemetry readout */}
            <div className="field-telemetry-strip" aria-label="Field size telemetry">
              <span className="telemetry-pill">
                <strong>{metrics.hectares.toFixed(2)}</strong> ha
              </span>
              <span className="telemetry-pill">
                <strong>{metrics.acres.toFixed(2)}</strong> acres
              </span>
              <span className="telemetry-pill">
                <strong>{metrics.perches.toFixed(1)}</strong> perches
              </span>
              <span className="telemetry-pill">
                <strong>{metrics.perimeterMeters.toLocaleString()}</strong> m perimeter
              </span>
            </div>

            <div className="field-coordinate-export">
              <label htmlFor="field-boundary-geojson">Boundary coordinates (GeoJSON Polygon)</label>
              <textarea
                id="field-boundary-geojson"
                value={boundaryGeoJson}
                readOnly
                rows={5}
                placeholder="Mark at least three points on the map to generate coordinates."
              />
              <small>Coordinates use [longitude, latitude] order and close back at the first point.</small>
              <button
                type="button"
                className="extension-secondary-button"
                onClick={() => void copyBoundaryCoordinates()}
                disabled={!boundaryGeoJson}
              >
                Copy coordinates
              </button>
            </div>
          </section>
        </div>

        <div className="field-map-layout">
          <div className="field-map-frame">
            <div className="field-map-commands">
              {/* Satellite / Hybrid / Street view toggle buttons */}
              <div
                className="map-layer-selector"
                role="radiogroup"
                aria-label="Map imagery basemap"
              >
                <button
                  type="button"
                  className={`map-layer-btn ${activeLayer === 'satellite' ? 'active' : ''}`}
                  onClick={() => switchLayer('satellite')}
                >
                  Satellite
                </button>
                <button
                  type="button"
                  className={`map-layer-btn ${activeLayer === 'hybrid' ? 'active' : ''}`}
                  onClick={() => switchLayer('hybrid')}
                >
                  Hybrid
                </button>
                <button
                  type="button"
                  className={`map-layer-btn ${activeLayer === 'streets' ? 'active' : ''}`}
                  onClick={() => switchLayer('streets')}
                >
                  Street View
                </button>
              </div>

              <span>Esri World Imagery · Drag vertices or click "+" to refine</span>
              <button type="button" onClick={fitSriLanka}>
                Show Sri Lanka
              </button>
            </div>
            <div
              className="field-map-canvas"
              ref={mapElement}
              aria-label="Map of paddy fields with satellite view"
            />
          </div>

          <aside className="field-map-sidebar">
            {data?.visibility === 'aggregate' ? (
              <>
                <h3>Regional field summary</h3>
                <p className="field-map-privacy">
                  Field boundaries and member identities are not shown to Buyers.
                </p>
                {data.regions.map((region) => (
                  <div className="field-summary-card" key={region.district}>
                    <strong>{region.district}</strong>
                    <span>
                      {region.verifiedFields} of {region.totalFields} fields verified
                    </span>
                    <small>{region.verifiedAreaHectares.toFixed(2)} verified ha</small>
                  </div>
                ))}
                {!data.regions.length && (
                  <p className="extension-empty">No regional field records are available.</p>
                )}
              </>
            ) : (
              <>
                <h3>{selectedField ? 'Field details' : 'Mapped fields'}</h3>
                {selectedField ? (
                  <div className="field-selected-card">
                    <button
                      type="button"
                      className="field-back-button"
                      onClick={() => setSelectedField(null)}
                    >
                      ← All fields
                    </button>
                    <strong>{selectedField.name}</strong>
                    <span>{selectedField.farmName}</span>
                    <span>
                      {selectedField.district || 'District not set'} ·{' '}
                      {selectedField.areaHectares.toFixed(2)} ha
                    </span>
                    <span
                      className={`field-status field-status-${selectedField.verificationStatus.toLowerCase()}`}
                    >
                      {selectedField.verificationStatus}
                    </span>
                    {role !== 'Farmer' && <small>Owner: {selectedField.ownerName}</small>}
                  </div>
                ) : (
                  data?.fields.map((field) => (
                    <button
                      type="button"
                      className="field-summary-card field-summary-button"
                      key={field.id}
                      onClick={() => {
                        setSelectedField(field)
                        if (field.latitude !== null && field.longitude !== null) {
                          map.current?.setView([field.latitude, field.longitude], 15)
                        }
                      }}
                    >
                      <strong>{field.name}</strong>
                      <span>
                        {field.farmName} · {field.district || 'District not set'}
                      </span>
                      <small>
                        {field.areaHectares.toFixed(2)} ha · {field.verificationStatus}
                      </small>
                    </button>
                  ))
                )}
                {!loading && !data?.fields.length && (
                  <p className="extension-empty">No field boundaries yet.</p>
                )}
              </>
            )}
          </aside>
        </div>
      </div>

      {canReview && (
        <section className="extension-card verification-queue">
          <div className="extension-toolbar">
            <div>
              <span className="panel-eyebrow">AGRARIAN REVIEW</span>
              <h2>Field verification queue</h2>
            </div>
            <span className="record-count">{queue.length} pending</span>
          </div>
          {queue.map((item) => (
            <article className="verification-row" key={item.id}>
              <div>
                <strong>
                  {item.fieldName} <span>· {item.farmName}</span>
                </strong>
                <small>
                  {item.ownerName} · {item.district || 'District not set'} ·{' '}
                  {item.areaHectares.toFixed(2)} ha
                </small>
                {item.reviewNotes && <p>{item.reviewNotes}</p>}
              </div>
              <div className="verification-actions">
                <input
                  aria-label={`Review note for ${item.fieldName}`}
                  placeholder="Reason required for return/reject"
                  value={reviewNotes[item.fieldId] || ''}
                  onChange={(event) =>
                    setReviewNotes((current) => ({
                      ...current,
                      [item.fieldId]: event.target.value,
                    }))
                  }
                />
                <button
                  type="button"
                  className="extension-primary-button"
                  disabled={busy}
                  onClick={() => void reviewField(item, 'Verified')}
                >
                  Verify
                </button>
                <button
                  type="button"
                  className="extension-secondary-button"
                  disabled={busy}
                  onClick={() => void reviewField(item, 'Returned')}
                >
                  Return
                </button>
                <button
                  type="button"
                  className="extension-danger-button"
                  disabled={busy}
                  onClick={() => void reviewField(item, 'Rejected')}
                >
                  Reject
                </button>
              </div>
            </article>
          ))}
          {!queue.length && <p className="extension-empty">No fields are waiting for review.</p>}
        </section>
      )}
    </div>
  )
}
