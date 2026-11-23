import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  ALL_SRI_LANKA_DISTRICTS,
  SRI_LANKA_PROVINCES,
  getDistrictsForProvince,
} from './sriLankaLocations'

// ---------------------------------------------------------
// TypeScript Definitions matching MarketplaceService DTOs
// ---------------------------------------------------------

export interface DemandItemDto {
  id: string
  demandId: string
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  grade: string
  quantityTonnes: number
  quantityKg: number
  offerPricePerKg: number
  notes: string | null
}

export interface DemandDto {
  id: string
  ownerId: string
  ownerName: string
  ownerBusinessName: string | null
  district: string
  season: string
  periodStart: string
  periodEnd: string
  status: string // 'Draft' | 'Open' | 'PartiallyFulfilled' | 'Fulfilled' | 'Cancelled'
  notes: string | null
  totalQuantityTonnes: number
  totalQuantityKg: number
  itemsCount: number
  purchaseRequestsCount: number
  demandItems: DemandItemDto[]
  createdAt: string
}

export interface MarketplaceSupplyItemDto {
  district: string
  province: string | null
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  availableStockKg: number
  availableStockMt: number
  forecastedSupplyMt: number
  latestCbslPricePerKg: number | null
  activeLotCount: number
  matchingDemandItemId: string | null
  suggestedFarmerId: string | null
}

export interface DistrictSupplyGapDto {
  district: string
  province: string | null
  totalDemandMt: number
  forecastedSupplyMt: number
  availableStockMt: number
  supplyGapMt: number
  gapStatus: string // 'Surplus' | 'Deficit' | 'Balanced'
}

export interface MarketplaceSupplyOverviewDto {
  supplyListings: MarketplaceSupplyItemDto[]
  regionalGaps: DistrictSupplyGapDto[]
  totalOpenDemandMt: number
  totalForecastedSupplyMt: number
  totalAvailableStockMt: number
  activePurchaseRequestsCount: number
}

export interface PurchaseRequestDto {
  id: string
  buyerId: string
  buyerName: string
  buyerBusinessName: string | null
  farmerId: string | null
  farmerName: string
  farmerPhone: string | null
  farmerEmail: string | null
  isFarmerContactRevealed: boolean
  demandItemId: string
  demandId: string | null
  demandSeason: string
  district: string
  riceVarietyId: string
  riceVarietyName: string
  varietyCode: string
  quantityKg: number
  quantityMt: number
  offerPricePerKg: number
  totalOrderValueLkr: number
  status: string // 'Pending' | 'Accepted' | 'Rejected' | 'Fulfilled' | 'Cancelled'
  terms: string | null
  deliveryTerms: string | null
  createdAt: string
}

export interface RiceVarietyItem {
  id: string
  varietyCode: string
  name: string
  description?: string | null
  maturityDays: number
  isActive: boolean
}

export interface SeasonItem {
  id: string
  name: string
  year: number
  isActive: boolean
  district?: string | null
}

// ---------------------------------------------------------
// Helper Utilities
// ---------------------------------------------------------

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

function safeNum(val: unknown): number {
  if (typeof val === 'number' && !Number.isNaN(val)) return val
  const n = Number(val)
  return Number.isNaN(n) ? 0 : n
}

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return '—'
  const date = new Date(dateStr.includes('T') ? dateStr : `${dateStr}T00:00:00`)
  return Number.isNaN(date.getTime())
    ? dateStr
    : new Intl.DateTimeFormat('en', { dateStyle: 'medium' }).format(date)
}

function formatCurrency(val: number): string {
  return `LKR ${safeNum(val).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function getDemandStatusBadge(status: string) {
  switch (status.toLowerCase()) {
    case 'open':
      return { label: 'Open Demand', className: 'status-positive' }
    case 'partiallyfulfilled':
      return { label: 'Partially Fulfilled', className: 'status-pending' }
    case 'fulfilled':
      return { label: 'Fulfilled', className: 'status-positive' }
    case 'cancelled':
      return { label: 'Cancelled', className: 'status-neutral' }
    case 'draft':
    default:
      return { label: status || 'Draft', className: 'status-neutral' }
  }
}

function getPrStatusBadge(status: string) {
  switch (status.toLowerCase()) {
    case 'pending':
      return { label: 'Pending Producer Acceptance', className: 'status-pending' }
    case 'accepted':
      return { label: 'Accepted (Trade Confirmed)', className: 'status-positive' }
    case 'fulfilled':
      return { label: 'Fulfilled & Delivered', className: 'status-positive' }
    case 'rejected':
      return { label: 'Declined', className: 'status-danger' }
    case 'cancelled':
      return { label: 'Cancelled', className: 'status-neutral' }
    default:
      return { label: status, className: 'status-neutral' }
  }
}

interface NewLineItem {
  tempId: string
  riceVarietyId: string
  grade: string
  quantityTonnes: string
  offerPricePerKg: string
  notes: string
}

// ---------------------------------------------------------
// MarketplacePage Component
// ---------------------------------------------------------

export default function MarketplacePage({ role = 'Farmer' }: { role?: string }) {
  const isBuyer = role === 'Buyer'
  const isFarmer = role === 'Farmer'
  const isAdminOrGov = ['SuperAdmin', 'Developer', 'Government', 'Officer'].includes(role)

  // Sub-navigation tab
  const [activeTab, setActiveTab] = useState<string>(() => {
    if (isBuyer) return 'supply'
    if (isFarmer) return 'requests'
    return 'gaps'
  })

  // Data states
  const [demands, setDemands] = useState<DemandDto[]>([])
  const [supplyOverview, setSupplyOverview] = useState<MarketplaceSupplyOverviewDto | null>(null)
  const [purchaseRequests, setPurchaseRequests] = useState<PurchaseRequestDto[]>([])
  const [varieties, setVarieties] = useState<RiceVarietyItem[]>([])
  const [seasons, setSeasons] = useState<SeasonItem[]>([])

  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  // Filters
  const [filterProvince, setFilterProvince] = useState('')
  const [filterDistrict, setFilterDistrict] = useState('')
  const [filterVarietyId, setFilterVarietyId] = useState('')

  // -------------------------------------------------------
  // Form State: Post New Demand (Buyer)
  // -------------------------------------------------------
  const [showDemandForm, setShowDemandForm] = useState(false)
  const [demandDistrict, setDemandDistrict] = useState('Polonnaruwa')
  const [demandSeason, setDemandSeason] = useState('Maha 2024/2025')
  const [demandStart, setDemandStart] = useState(() => new Date().toISOString().split('T')[0])
  const [demandEnd, setDemandEnd] = useState(() => {
    const d = new Date()
    d.setMonth(d.getMonth() + 4)
    return d.toISOString().split('T')[0]
  })
  const [demandNotes, setDemandNotes] = useState('')
  const [lineItems, setLineItems] = useState<NewLineItem[]>([
    {
      tempId: 'item-1',
      riceVarietyId: '',
      grade: 'Grade A (<14% moisture)',
      quantityTonnes: '25',
      offerPricePerKg: '220',
      notes: 'Direct farm-gate pickup preferred',
    },
  ])

  // -------------------------------------------------------
  // Modal / Form State: Initiate Purchase Request (Buyer)
  // -------------------------------------------------------
  const [prModalOpen, setPrModalOpen] = useState(false)
  const [prSelectedSupply, setPrSelectedSupply] = useState<MarketplaceSupplyItemDto | null>(null)
  const [prDemandItemId, setPrDemandItemId] = useState('')
  const [prQuantityKg, setPrQuantityKg] = useState('5000')
  const [prOfferPrice, setPrOfferPrice] = useState('225')
  const [prTerms, setPrTerms] = useState('Cash on delivery after quality testing')
  const [prDeliveryTerms, setPrDeliveryTerms] = useState('Ex-farm gate / Buyer transports')

  // Load active active varieties and seasons
  const activeVarieties = useMemo(() => varieties.filter((v) => v.isActive), [varieties])
  const activeSeasons = useMemo(() => seasons.filter((s) => s.isActive), [seasons])

  // Districts for selected province
  const availableDistricts = useMemo(() => {
    return getDistrictsForProvince(filterProvince)
  }, [filterProvince])

  // -------------------------------------------------------
  // Data Fetching
  // -------------------------------------------------------
  const loadAll = useCallback(async () => {
    const [demandsData, supplyData, prData, varietiesData, seasonsData] = await Promise.all([
      requestJson<DemandDto[]>('/api/demands').catch(() => [] as DemandDto[]),
      requestJson<MarketplaceSupplyOverviewDto>('/api/marketplace/supply').catch(() => null),
      requestJson<PurchaseRequestDto[]>('/api/purchase-requests').catch(() => [] as PurchaseRequestDto[]),
      requestJson<RiceVarietyItem[]>('/api/rice-varieties').catch(() => [] as RiceVarietyItem[]),
      requestJson<SeasonItem[]>('/api/seasons').catch(() => [] as SeasonItem[]),
    ])

    return {
      demands: Array.isArray(demandsData) ? demandsData : [],
      supplyOverview: supplyData,
      purchaseRequests: Array.isArray(prData) ? prData : [],
      varieties: Array.isArray(varietiesData) ? varietiesData : [],
      seasons: Array.isArray(seasonsData) ? seasonsData : [],
    }
  }, [])

  const refreshMarketplace = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const data = await loadAll()
      setDemands(data.demands)
      setSupplyOverview(data.supplyOverview)
      setPurchaseRequests(data.purchaseRequests)
      setVarieties(data.varieties)
      setSeasons(data.seasons)

      // Set default variety on line items if needed
      if (data.varieties.length > 0) {
        const firstVar = data.varieties[0].id
        setLineItems((prev) =>
          prev.map((item) => (item.riceVarietyId ? item : { ...item, riceVarietyId: firstVar }))
        )
      }
      if (data.seasons.length > 0) {
        const activeS = data.seasons.find((s) => s.isActive) || data.seasons[0]
        setDemandSeason(activeS.name)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch marketplace records.')
    } finally {
      setLoading(false)
    }
  }, [loadAll])

  useEffect(() => {
    void refreshMarketplace()
  }, [refreshMarketplace])

  // -------------------------------------------------------
  // Buyer Actions
  // -------------------------------------------------------

  const handleAddLineItem = () => {
    const defaultVarId = activeVarieties[0]?.id || ''
    setLineItems((prev) => [
      ...prev,
      {
        tempId: `item-${Date.now()}`,
        riceVarietyId: defaultVarId,
        grade: 'Grade A (<14% moisture)',
        quantityTonnes: '10',
        offerPricePerKg: '220',
        notes: '',
      },
    ])
  }

  const handleRemoveLineItem = (tempId: string) => {
    if (lineItems.length <= 1) return
    setLineItems((prev) => prev.filter((i) => i.tempId !== tempId))
  }

  const handleLineItemChange = (
    tempId: string,
    field: keyof NewLineItem,
    value: string
  ) => {
    setLineItems((prev) =>
      prev.map((item) => (item.tempId === tempId ? { ...item, [field]: value } : item))
    )
  }

  // Calculated totals for new demand
  const totalDemandTonnes = useMemo(() => {
    return lineItems.reduce((sum, item) => sum + safeNum(item.quantityTonnes), 0)
  }, [lineItems])

  const totalEstimatedCostLkr = useMemo(() => {
    return lineItems.reduce((sum, item) => {
      const kg = safeNum(item.quantityTonnes) * 1000
      const price = safeNum(item.offerPricePerKg)
      return sum + kg * price
    }, 0)
  }, [lineItems])

  const handlePostDemand = async (e: FormEvent) => {
    e.preventDefault()
    if (!demandDistrict || !demandSeason) {
      setError('Please select district and season for the demand.')
      return
    }

    if (lineItems.length === 0) {
      setError('Add at least one rice variety requirement to your demand.')
      return
    }

    for (const item of lineItems) {
      if (!item.riceVarietyId) {
        setError('Please choose a rice variety for all line items.')
        return
      }
      if (safeNum(item.quantityTonnes) <= 0) {
        setError('Quantity in tonnes must be greater than zero.')
        return
      }
    }

    setBusy(true)
    setError('')
    setNotice('')

    try {
      const payload = {
        district: demandDistrict.trim(),
        season: demandSeason.trim(),
        periodStart: demandStart,
        periodEnd: demandEnd,
        notes: demandNotes.trim() || undefined,
        items: lineItems.map((item) => ({
          riceVarietyId: item.riceVarietyId,
          quantityTonnes: safeNum(item.quantityTonnes),
          quantityKg: safeNum(item.quantityTonnes) * 1000,
          grade: item.grade.trim() || 'Grade A',
          offerPricePerKg: safeNum(item.offerPricePerKg) || undefined,
          notes: item.notes.trim() || undefined,
        })),
      }

      await requestJson<DemandDto>('/api/demands', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })

      setNotice('Procurement demand posted successfully to the Swarnabhoomi marketplace.')
      setShowDemandForm(false)
      setActiveTab('my-demands')
      await refreshMarketplace()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to publish demand.')
    } finally {
      setBusy(false)
    }
  }

  const handleCancelDemand = async (demandId: string) => {
    if (!window.confirm('Are you sure you want to cancel this demand?')) return
    setBusy(true)
    setError('')
    try {
      await requestJson(`/api/demands/${demandId}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: 'Cancelled' }),
      })
      setNotice('Demand marked as Cancelled.')
      await refreshMarketplace()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not cancel demand.')
    } finally {
      setBusy(false)
    }
  }

  // Open modal to initiate Purchase Request from Supply Board
  const openPrModal = (supplyItem: MarketplaceSupplyItemDto) => {
    setPrSelectedSupply(supplyItem)
    setPrOfferPrice(
      supplyItem.latestCbslPricePerKg ? supplyItem.latestCbslPricePerKg.toString() : '220'
    )
    setPrQuantityKg(
      supplyItem.availableStockKg > 0
        ? Math.min(5000, supplyItem.availableStockKg).toString()
        : '5000'
    )

    // Match with existing demand item if buyer has an open demand with this variety
    const openDemandItem = demands
      .flatMap((d) => d.demandItems)
      .find((di) => di.riceVarietyId === supplyItem.riceVarietyId)

    setPrDemandItemId(openDemandItem?.id || '')
    setPrModalOpen(true)
  }

  const handleSubmitPurchaseRequest = async (e: FormEvent) => {
    e.preventDefault()
    if (!prSelectedSupply) return

    if (!prDemandItemId) {
      setError(
        'Please link this request to one of your active Demand Items, or post a Demand first.'
      )
      return
    }

    if (safeNum(prQuantityKg) <= 0 || safeNum(prOfferPrice) <= 0) {
      setError('Please enter a valid quantity and offer price.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')

    try {
      const payload = {
        demandItemId: prDemandItemId,
        quantityKg: safeNum(prQuantityKg),
        offerPricePerKg: safeNum(prOfferPrice),
        farmerId: prSelectedSupply.suggestedFarmerId || null,
        terms: prTerms.trim() || undefined,
        deliveryTerms: prDeliveryTerms.trim() || undefined,
      }

      await requestJson<PurchaseRequestDto>('/api/purchase-requests', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })

      setNotice(
        'Purchase order sent! The producer has been notified. Producer details will be revealed upon acceptance.'
      )
      setPrModalOpen(false)
      setActiveTab('orders')
      await refreshMarketplace()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit purchase request.')
    } finally {
      setBusy(false)
    }
  }

  // Update PR status (Buyer cancels or fulfills, Farmer accepts/rejects/fulfills)
  const handleUpdatePrStatus = async (
    prId: string,
    newStatus: string,
    promptMsg?: string
  ) => {
    if (promptMsg && !window.confirm(promptMsg)) return

    setBusy(true)
    setError('')
    setNotice('')

    try {
      await requestJson(`/api/purchase-requests/${prId}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: newStatus }),
      })

      setNotice(`Order status successfully updated to '${newStatus}'.`)
      await refreshMarketplace()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update order status.')
    } finally {
      setBusy(false)
    }
  }

  // -------------------------------------------------------
  // Filtered Supply & Demands
  // -------------------------------------------------------
  const filteredSupply = useMemo(() => {
    const list = supplyOverview?.supplyListings || []
    return list.filter((item) => {
      if (filterProvince && item.province !== filterProvince) return false
      if (filterDistrict && item.district.toLowerCase() !== filterDistrict.toLowerCase())
        return false
      if (filterVarietyId && item.riceVarietyId !== filterVarietyId) return false
      return true
    })
  }, [supplyOverview, filterProvince, filterDistrict, filterVarietyId])

  const filteredDemands = useMemo(() => {
    return demands.filter((demand) => {
      if (filterDistrict && demand.district.toLowerCase() !== filterDistrict.toLowerCase())
        return false
      return true
    })
  }, [demands, filterDistrict])

  // Buyer's all demand items available for linking in PR modal
  const buyerActiveDemandItems = useMemo(() => {
    return demands
      .filter((d) => d.status === 'Open' || d.status === 'PartiallyFulfilled')
      .flatMap((d) =>
        d.demandItems.map((item) => ({
          ...item,
          season: d.season,
          district: d.district,
        }))
      )
  }, [demands])

  return (
    <div className="extension-page marketplace-page">
      {/* Top Banner Card */}
      <section className="extension-card marketplace-hero-card">
        <div className="extension-toolbar">
          <div>
            <span className="panel-eyebrow">AGRICULTURAL COMMODITY EXCHANGE</span>
            <h2>Rice Marketplace & Procurement</h2>
            <p>
              Direct, transparent trade connecting verified paddy harvests, real-time central bank
              prices, and role-encapsulated buyer demands across Sri Lanka.
            </p>
          </div>
          <div className="toolbar-button-group">
            {isBuyer && (
              <button
                type="button"
                className="extension-primary-button"
                onClick={() => setShowDemandForm((prev) => !prev)}
              >
                {showDemandForm ? 'Close Demand Form' : '+ Post New Demand'}
              </button>
            )}
            <button
              type="button"
              className="extension-secondary-button"
              onClick={() => void refreshMarketplace()}
              disabled={loading || busy}
            >
              {loading ? 'Refreshing…' : 'Refresh Market'}
            </button>
          </div>
        </div>

        {/* Global Alert Banners */}
        {error && (
          <div className="extension-alert extension-alert-error" role="alert">
            <strong>Action Alert:</strong> {error}
            <button
              type="button"
              className="alert-dismiss-btn"
              onClick={() => setError('')}
              aria-label="Dismiss"
            >
              ✕
            </button>
          </div>
        )}

        {notice && (
          <div className="extension-alert extension-alert-success" role="status">
            <strong>Update:</strong> {notice}
            <button
              type="button"
              className="alert-dismiss-btn"
              onClick={() => setNotice('')}
              aria-label="Dismiss"
            >
              ✕
            </button>
          </div>
        )}

        {/* Sub-navigation Tabs based on Role */}
        <div className="subnav-tabs-strip">
          {isBuyer && (
            <>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'supply' ? 'active' : ''}`}
                onClick={() => setActiveTab('supply')}
              >
                🌾 Verified Supply & CBSL Prices
              </button>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'my-demands' ? 'active' : ''}`}
                onClick={() => setActiveTab('my-demands')}
              >
                📋 My Demands ({demands.length})
              </button>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'orders' ? 'active' : ''}`}
                onClick={() => setActiveTab('orders')}
              >
                📦 Purchase Orders & Privacy Tracker ({purchaseRequests.length})
              </button>
            </>
          )}

          {isFarmer && (
            <>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'requests' ? 'active' : ''}`}
                onClick={() => setActiveTab('requests')}
              >
                📥 Incoming Purchase Requests ({purchaseRequests.length})
              </button>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'demands' ? 'active' : ''}`}
                onClick={() => setActiveTab('demands')}
              >
                📊 Regional Market Demand Signals ({demands.length})
              </button>
            </>
          )}

          {isAdminOrGov && (
            <>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'gaps' ? 'active' : ''}`}
                onClick={() => setActiveTab('gaps')}
              >
                ⚖️ National Supply vs Demand Gaps
              </button>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'all-demands' ? 'active' : ''}`}
                onClick={() => setActiveTab('all-demands')}
              >
                📜 All Market Demands ({demands.length})
              </button>
              <button
                type="button"
                className={`subnav-tab ${activeTab === 'all-orders' ? 'active' : ''}`}
                onClick={() => setActiveTab('all-orders')}
              >
                🤝 Trade Orders & Agreements ({purchaseRequests.length})
              </button>
            </>
          )}
        </div>
      </section>

      {/* =================================================== */}
      {/* BUYER: Post New Demand Form (Accordion / Collapsible)*/}
      {/* =================================================== */}
      {isBuyer && showDemandForm && (
        <section className="extension-card demand-create-panel">
          <div className="panel-header">
            <h3>Publish Buyer Demand Requirement</h3>
            <p>
              Specify your target district, season, delivery period, and multiple rice varieties.
              Registered district farmers and millers can inspect your requirements and supply matching stocks.
            </p>
          </div>

          <form onSubmit={handlePostDemand} className="extension-form">
            <div className="form-row-2col">
              <label>
                Procurement District *
                <input
                  list="marketplace-districts"
                  value={demandDistrict}
                  onChange={(e) => setDemandDistrict(e.target.value)}
                  placeholder="e.g. Polonnaruwa, Anuradhapura"
                  required
                />
                <datalist id="marketplace-districts">
                  {ALL_SRI_LANKA_DISTRICTS.map((d) => (
                    <option key={d} value={d} />
                  ))}
                </datalist>
              </label>

              <label>
                Cultivation Season *
                <select
                  value={demandSeason}
                  onChange={(e) => setDemandSeason(e.target.value)}
                  required
                >
                  {activeSeasons.length > 0 ? (
                    activeSeasons.map((s) => (
                      <option key={s.id} value={s.name}>
                        {s.name} ({s.year})
                      </option>
                    ))
                  ) : (
                    <>
                      <option value="Maha 2024/2025">Maha 2024/2025</option>
                      <option value="Yala 2025">Yala 2025</option>
                    </>
                  )}
                </select>
              </label>
            </div>

            <div className="form-row-2col">
              <label>
                Procurement Window Start *
                <input
                  type="date"
                  value={demandStart}
                  onChange={(e) => setDemandStart(e.target.value)}
                  required
                />
              </label>
              <label>
                Procurement Window End *
                <input
                  type="date"
                  value={demandEnd}
                  onChange={(e) => setDemandEnd(e.target.value)}
                  required
                />
              </label>
            </div>

            <label>
              Delivery & Quality Specification Notes
              <input
                type="text"
                value={demandNotes}
                onChange={(e) => setDemandNotes(e.target.value)}
                placeholder="e.g. Moisture must be <14%, ex-farm delivery or warehouse dropoff"
              />
            </label>

            {/* Dynamic Variety Line Items */}
            <div className="demand-items-section">
              <div className="demand-items-header">
                <div>
                  <strong>Required Rice Varieties & Target Prices</strong>
                  <p>Add one or multiple rice varieties needed for this demand window.</p>
                </div>
                <button
                  type="button"
                  className="extension-secondary-button"
                  onClick={handleAddLineItem}
                >
                  + Add Another Variety
                </button>
              </div>

              <div className="table-scroll">
                <table className="demand-line-items-table">
                  <thead>
                    <tr>
                      <th style={{ width: '28%' }}>Rice Variety *</th>
                      <th style={{ width: '20%' }}>Grade / Quality</th>
                      <th style={{ width: '15%' }}>Quantity (Tonnes) *</th>
                      <th style={{ width: '18%' }}>Offer Price (LKR / kg)</th>
                      <th>Notes</th>
                      <th style={{ width: '60px' }}>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {lineItems.map((item) => (
                      <tr key={item.tempId}>
                        <td>
                          <select
                            value={item.riceVarietyId}
                            onChange={(e) =>
                              handleLineItemChange(item.tempId, 'riceVarietyId', e.target.value)
                            }
                            required
                          >
                            <option value="">-- Choose Variety --</option>
                            {varieties.map((v) => (
                              <option key={v.id} value={v.id}>
                                {v.name} ({v.varietyCode})
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <input
                            type="text"
                            value={item.grade}
                            onChange={(e) =>
                              handleLineItemChange(item.tempId, 'grade', e.target.value)
                            }
                            placeholder="Grade A, Samba, etc."
                          />
                        </td>
                        <td>
                          <input
                            type="number"
                            min="0.1"
                            step="0.1"
                            value={item.quantityTonnes}
                            onChange={(e) =>
                              handleLineItemChange(item.tempId, 'quantityTonnes', e.target.value)
                            }
                            required
                          />
                        </td>
                        <td>
                          <input
                            type="number"
                            min="1"
                            step="0.5"
                            value={item.offerPricePerKg}
                            onChange={(e) =>
                              handleLineItemChange(item.tempId, 'offerPricePerKg', e.target.value)
                            }
                            placeholder="e.g. 220"
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={item.notes}
                            onChange={(e) =>
                              handleLineItemChange(item.tempId, 'notes', e.target.value)
                            }
                            placeholder="Optional notes"
                          />
                        </td>
                        <td>
                          <button
                            type="button"
                            className="extension-danger-button line-remove-btn"
                            disabled={lineItems.length <= 1}
                            onClick={() => handleRemoveLineItem(item.tempId)}
                            title="Remove line item"
                          >
                            ✕
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Summary Bar */}
              <div className="demand-summary-strip">
                <div className="summary-pill">
                  <span>Total Requirement:</span>
                  <strong>{totalDemandTonnes.toFixed(1)} MT ({safeNum(totalDemandTonnes * 1000).toLocaleString()} kg)</strong>
                </div>
                <div className="summary-pill">
                  <span>Est. Outlay:</span>
                  <strong>{formatCurrency(totalEstimatedCostLkr)}</strong>
                </div>
                <div className="summary-pill">
                  <span>Variety Count:</span>
                  <strong>{lineItems.length} lines</strong>
                </div>
              </div>
            </div>

            <div className="form-action-strip">
              <button
                type="submit"
                className="extension-primary-button"
                disabled={busy}
              >
                {busy ? 'Publishing Demand…' : 'Publish Demand to Marketplace'}
              </button>
              <button
                type="button"
                className="extension-secondary-button"
                onClick={() => setShowDemandForm(false)}
              >
                Cancel
              </button>
            </div>
          </form>
        </section>
      )}

      {/* =================================================== */}
      {/* BUYER TAB: Verified Supply Board & CBSL Prices      */}
      {/* =================================================== */}
      {isBuyer && activeTab === 'supply' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>Verified Available Supply & Official CBSL Benchmark</h3>
              <p>
                Real-time paddy inventory in farmer storage and projected harvest yields from GPS-verified fields.
                Official Central Bank of Sri Lanka (CBSL) wholesale prices are shown alongside to ensure transparent trading.
              </p>
            </div>
          </div>

          {/* Filter Bar */}
          <div className="cultivation-filter-strip marketplace-filters">
            <label>
              Province:
              <select
                value={filterProvince}
                onChange={(e) => {
                  setFilterProvince(e.target.value)
                  setFilterDistrict('')
                }}
              >
                <option value="">All 9 Provinces</option>
                {SRI_LANKA_PROVINCES.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </select>
            </label>

            <label>
              District:
              <select
                value={filterDistrict}
                onChange={(e) => setFilterDistrict(e.target.value)}
              >
                <option value="">All Districts</option>
                {availableDistricts.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Rice Variety:
              <select
                value={filterVarietyId}
                onChange={(e) => setFilterVarietyId(e.target.value)}
              >
                <option value="">All Varieties</option>
                {varieties.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.name} ({v.varietyCode})
                  </option>
                ))}
              </select>
            </label>
          </div>

          {filteredSupply.length === 0 ? (
            <div className="empty-state-card">
              <p>No verified supply listings match the selected filters.</p>
            </div>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>DISTRICT / PROVINCE</th>
                    <th>VARIETY & CODE</th>
                    <th>STORED STOCK (INVENTORY)</th>
                    <th>PROJECTED HARVEST</th>
                    <th>CBSL WHOLESALE BENCHMARK</th>
                    <th>ACTIVE LOTS</th>
                    <th>TRADE ACTION</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredSupply.map((item, idx) => (
                    <tr key={`${item.district}-${item.riceVarietyId}-${idx}`}>
                      <td>
                        <strong className="table-primary">{item.district}</strong>
                        <span className="table-secondary">{item.province || 'Sri Lanka'}</span>
                      </td>
                      <td>
                        <strong className="table-primary">{item.riceVarietyName}</strong>
                        <span className="code-chip">{item.varietyCode}</span>
                      </td>
                      <td>
                        <strong>{safeNum(item.availableStockMt).toFixed(1)} MT</strong>
                        <span className="table-secondary">
                          {safeNum(item.availableStockKg).toLocaleString()} kg ready
                        </span>
                      </td>
                      <td>
                        <span className="forecast-chip">
                          {safeNum(item.forecastedSupplyMt).toFixed(1)} MT
                        </span>
                        <span className="table-secondary">Upcoming harvest</span>
                      </td>
                      <td>
                        {item.latestCbslPricePerKg ? (
                          <div>
                            <strong className="price-tag">
                              {formatCurrency(item.latestCbslPricePerKg)} / kg
                            </strong>
                            <span className="table-secondary">Official CBSL</span>
                          </div>
                        ) : (
                          <span className="table-secondary">Pending update</span>
                        )}
                      </td>
                      <td>
                        <span className="status-neutral status-badge">
                          {item.activeLotCount} active {item.activeLotCount === 1 ? 'batch' : 'batches'}
                        </span>
                      </td>
                      <td>
                        <button
                          type="button"
                          className="extension-primary-button pr-init-btn"
                          onClick={() => openPrModal(item)}
                        >
                          Request Purchase
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* =================================================== */}
      {/* BUYER TAB: My Demands List                          */}
      {/* =================================================== */}
      {isBuyer && activeTab === 'my-demands' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>My Active Demands & Procurement Targets</h3>
              <p>
                Demands currently broadcast to regional farmers. When farmers accept purchase orders,
                matching quantities are reserved.
              </p>
            </div>
            <button
              type="button"
              className="extension-primary-button"
              onClick={() => setShowDemandForm(true)}
            >
              + Post Another Demand
            </button>
          </div>

          {demands.length === 0 ? (
            <div className="empty-state-card">
              <p>You haven't posted any procurement demands yet.</p>
              <button
                type="button"
                className="extension-primary-button"
                onClick={() => setShowDemandForm(true)}
                style={{ marginTop: '12px' }}
              >
                Post Your First Demand
              </button>
            </div>
          ) : (
            <div className="demand-cards-grid">
              {demands.map((demand) => {
                const statusBadge = getDemandStatusBadge(demand.status)
                const isOpen = demand.status === 'Open' || demand.status === 'PartiallyFulfilled'

                return (
                  <div key={demand.id} className="demand-card">
                    <div className="demand-card-header">
                      <div>
                        <span className="panel-eyebrow">{demand.season}</span>
                        <h4>{demand.district} Procurement</h4>
                      </div>
                      <span className={`status-badge ${statusBadge.className}`}>
                        {statusBadge.label}
                      </span>
                    </div>

                    <div className="demand-card-body">
                      <div className="demand-meta-row">
                        <span>Window:</span>
                        <strong>{formatDate(demand.periodStart)} — {formatDate(demand.periodEnd)}</strong>
                      </div>
                      <div className="demand-meta-row">
                        <span>Total Requirement:</span>
                        <strong>{safeNum(demand.totalQuantityTonnes).toFixed(1)} MT ({safeNum(demand.totalQuantityKg).toLocaleString()} kg)</strong>
                      </div>
                      {demand.notes && (
                        <p className="demand-notes">
                          <em>Note:</em> {demand.notes}
                        </p>
                      )}

                      {/* Items Table */}
                      <div className="demand-card-items">
                        <span className="items-title">Required Varieties ({demand.demandItems.length}):</span>
                        <div className="table-scroll">
                          <table className="mini-items-table">
                            <thead>
                              <tr>
                                <th>Variety</th>
                                <th>Grade</th>
                                <th>Target</th>
                                <th>Offer Price</th>
                              </tr>
                            </thead>
                            <tbody>
                              {demand.demandItems.map((di) => (
                                <tr key={di.id}>
                                  <td><strong>{di.riceVarietyName}</strong></td>
                                  <td>{di.grade}</td>
                                  <td>{safeNum(di.quantityTonnes).toFixed(1)} MT</td>
                                  <td>{di.offerPricePerKg ? formatCurrency(di.offerPricePerKg) : 'Negotiable'}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    </div>

                    <div className="demand-card-footer">
                      <span className="table-secondary">Created {formatDate(demand.createdAt)}</span>
                      {isOpen && (
                        <button
                          type="button"
                          className="extension-danger-button"
                          disabled={busy}
                          onClick={() => handleCancelDemand(demand.id)}
                        >
                          Cancel Demand
                        </button>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </section>
      )}

      {/* =================================================== */}
      {/* BUYER TAB: Purchase Orders & Privacy Tracker        */}
      {/* =================================================== */}
      {isBuyer && activeTab === 'orders' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>Purchase Orders & Trade Matches</h3>
              <p>
                Strict privacy protection is enforced: Producer contact details remain masked and confidential
                until the producer confirms and accepts your order.
              </p>
            </div>
          </div>

          {purchaseRequests.length === 0 ? (
            <div className="empty-state-card">
              <p>You haven't initiated any purchase requests yet.</p>
              <button
                type="button"
                className="extension-primary-button"
                onClick={() => setActiveTab('supply')}
                style={{ marginTop: '12px' }}
              >
                Browse Supply Board
              </button>
            </div>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>ORDER ID & DATE</th>
                    <th>VARIETY & DISTRICT</th>
                    <th>QUANTITY (KG / MT)</th>
                    <th>OFFER PRICE & VALUE</th>
                    <th>PRODUCER CONTACT (PRIVACY PROTECTED)</th>
                    <th>STATUS</th>
                    <th>ACTIONS</th>
                  </tr>
                </thead>
                <tbody>
                  {purchaseRequests.map((pr) => {
                    const prBadge = getPrStatusBadge(pr.status)
                    const isAccepted = pr.status === 'Accepted'
                    const isPending = pr.status === 'Pending'

                    return (
                      <tr key={pr.id}>
                        <td>
                          <strong className="table-primary">{pr.id.substring(0, 8).toUpperCase()}</strong>
                          <span className="table-secondary">{formatDate(pr.createdAt)}</span>
                        </td>
                        <td>
                          <strong className="table-primary">{pr.riceVarietyName}</strong>
                          <span className="table-secondary">{pr.district} · {pr.demandSeason}</span>
                        </td>
                        <td>
                          <strong>{safeNum(pr.quantityKg).toLocaleString()} kg</strong>
                          <span className="table-secondary">({safeNum(pr.quantityMt).toFixed(2)} MT)</span>
                        </td>
                        <td>
                          <strong>{formatCurrency(pr.offerPricePerKg)} / kg</strong>
                          <span className="table-secondary">Total: {formatCurrency(pr.totalOrderValueLkr)}</span>
                        </td>
                        <td>
                          {pr.isFarmerContactRevealed ? (
                            <div className="revealed-contact-box">
                              <span className="revealed-tag">✓ Producer Accepted</span>
                              <strong>{pr.farmerName}</strong>
                              {pr.farmerPhone && (
                                <a href={`tel:${pr.farmerPhone}`} className="contact-link">
                                  📞 {pr.farmerPhone}
                                </a>
                              )}
                              {pr.farmerEmail && (
                                <a href={`mailto:${pr.farmerEmail}`} className="contact-link">
                                  ✉️ {pr.farmerEmail}
                                </a>
                              )}
                            </div>
                          ) : (
                            <div className="privacy-masked-box">
                              <span className="privacy-badge">
                                🛡️ Identity Confidential
                              </span>
                              <span className="masked-label">{pr.farmerName}</span>
                              <small className="table-secondary">
                                Hidden until producer accepts
                              </small>
                            </div>
                          )}
                        </td>
                        <td>
                          <span className={`status-badge ${prBadge.className}`}>
                            {prBadge.label}
                          </span>
                        </td>
                        <td>
                          <div className="user-action-buttons">
                            {isPending && (
                              <button
                                type="button"
                                className="extension-danger-button"
                                disabled={busy}
                                onClick={() =>
                                  handleUpdatePrStatus(
                                    pr.id,
                                    'Cancelled',
                                    'Are you sure you want to cancel this pending order?'
                                  )
                                }
                              >
                                Cancel Order
                              </button>
                            )}
                            {isAccepted && (
                              <button
                                type="button"
                                className="extension-primary-button"
                                disabled={busy}
                                onClick={() =>
                                  handleUpdatePrStatus(
                                    pr.id,
                                    'Fulfilled',
                                    'Confirm order receipt and payment? This completes the transaction and updates inventory.'
                                  )
                                }
                              >
                                Mark Fulfilled
                              </button>
                            )}
                          </div>
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

      {/* =================================================== */}
      {/* FARMER TAB: Incoming Purchase Requests Queue        */}
      {/* =================================================== */}
      {isFarmer && activeTab === 'requests' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>Incoming Purchase Requests & Mill Orders</h3>
              <p>
                Buyers and rice millers have submitted direct purchase requests for your harvested and stored paddy.
                Accepting a request confirms the trade and shares your contact details to coordinate farm-gate delivery.
              </p>
            </div>
          </div>

          {purchaseRequests.length === 0 ? (
            <div className="empty-state-card">
              <p>No active purchase requests currently in your queue.</p>
              <span className="table-secondary">
                Ensure your harvested paddy is recorded under Stored Stock so buyers can discover your supply.
              </span>
            </div>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>BUYER / BUSINESS</th>
                    <th>VARIETY & SEASON</th>
                    <th>ORDER QUANTITY</th>
                    <th>OFFERED PRICE & TOTAL</th>
                    <th>TERMS & DELIVERY</th>
                    <th>STATUS</th>
                    <th>PRODUCER DECISION</th>
                  </tr>
                </thead>
                <tbody>
                  {purchaseRequests.map((pr) => {
                    const prBadge = getPrStatusBadge(pr.status)
                    const isPending = pr.status === 'Pending'
                    const isAccepted = pr.status === 'Accepted'

                    return (
                      <tr key={pr.id}>
                        <td>
                          <strong className="table-primary">
                            {pr.buyerBusinessName || pr.buyerName}
                          </strong>
                          <span className="table-secondary">{pr.buyerName}</span>
                        </td>
                        <td>
                          <strong className="table-primary">{pr.riceVarietyName}</strong>
                          <span className="table-secondary">{pr.district} · {pr.demandSeason}</span>
                        </td>
                        <td>
                          <strong>{safeNum(pr.quantityKg).toLocaleString()} kg</strong>
                          <span className="table-secondary">({safeNum(pr.quantityMt).toFixed(2)} MT)</span>
                        </td>
                        <td>
                          <strong className="price-tag">{formatCurrency(pr.offerPricePerKg)} / kg</strong>
                          <span className="table-secondary">Total: {formatCurrency(pr.totalOrderValueLkr)}</span>
                        </td>
                        <td>
                          <div>
                            <span className="table-secondary">
                              <strong>Terms:</strong> {pr.terms || 'Standard trade'}
                            </span>
                            <span className="table-secondary">
                              <strong>Delivery:</strong> {pr.deliveryTerms || 'Farm gate'}
                            </span>
                          </div>
                        </td>
                        <td>
                          <span className={`status-badge ${prBadge.className}`}>
                            {prBadge.label}
                          </span>
                        </td>
                        <td>
                          <div className="user-action-buttons">
                            {isPending && (
                              <>
                                <button
                                  type="button"
                                  className="user-approve-button"
                                  disabled={busy}
                                  onClick={() =>
                                    handleUpdatePrStatus(
                                      pr.id,
                                      'Accepted',
                                      `Accept order of ${pr.quantityKg} kg at ${pr.offerPricePerKg} LKR/kg? This will reveal your contact information to ${pr.buyerName}.`
                                    )
                                  }
                                >
                                  Accept Order
                                </button>
                                <button
                                  type="button"
                                  className="user-reject-button"
                                  disabled={busy}
                                  onClick={() =>
                                    handleUpdatePrStatus(
                                      pr.id,
                                      'Rejected',
                                      'Decline this purchase order?'
                                    )
                                  }
                                >
                                  Decline
                                </button>
                              </>
                            )}

                            {isAccepted && (
                              <button
                                type="button"
                                className="extension-primary-button"
                                disabled={busy}
                                onClick={() =>
                                  handleUpdatePrStatus(
                                    pr.id,
                                    'Fulfilled',
                                    'Confirm delivery and settlement? This will deduct the sold quantity from your recorded paddy stock.'
                                  )
                                }
                              >
                                Confirm Delivery (Fulfill)
                              </button>
                            )}
                          </div>
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

      {/* =================================================== */}
      {/* FARMER TAB: Regional Market Demand Signals          */}
      {/* =================================================== */}
      {isFarmer && activeTab === 'demands' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>Regional Market Demand Signals</h3>
              <p>
                Browse open buyer demands across Sri Lanka to see which rice varieties and grades have active commercial demand
                and benchmark your selling prices.
              </p>
            </div>
          </div>

          {/* District filter */}
          <div className="cultivation-filter-strip marketplace-filters">
            <label>
              Filter by District:
              <select
                value={filterDistrict}
                onChange={(e) => setFilterDistrict(e.target.value)}
              >
                <option value="">All Sri Lankan Districts</option>
                {ALL_SRI_LANKA_DISTRICTS.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </label>
          </div>

          {filteredDemands.length === 0 ? (
            <div className="empty-state-card">
              <p>No active market demands found for the selected filter.</p>
            </div>
          ) : (
            <div className="demand-cards-grid">
              {filteredDemands.map((demand) => (
                <div key={demand.id} className="demand-card">
                  <div className="demand-card-header">
                    <div>
                      <span className="panel-eyebrow">{demand.season}</span>
                      <h4>{demand.district} Market Demand</h4>
                    </div>
                    <span className="status-badge status-positive">Open Requirement</span>
                  </div>

                  <div className="demand-card-body">
                    <div className="demand-meta-row">
                      <span>Buyer:</span>
                      <strong>{demand.ownerBusinessName || 'Verified Commercial Buyer'}</strong>
                    </div>
                    <div className="demand-meta-row">
                      <span>Delivery Window:</span>
                      <strong>{formatDate(demand.periodStart)} — {formatDate(demand.periodEnd)}</strong>
                    </div>
                    <div className="demand-meta-row">
                      <span>Total Needed:</span>
                      <strong>{safeNum(demand.totalQuantityTonnes).toFixed(1)} MT ({safeNum(demand.totalQuantityKg).toLocaleString()} kg)</strong>
                    </div>

                    <div className="demand-card-items">
                      <span className="items-title">Varieties Needed:</span>
                      <div className="table-scroll">
                        <table className="mini-items-table">
                          <thead>
                            <tr>
                              <th>Variety</th>
                              <th>Grade</th>
                              <th>Quantity</th>
                              <th>Buyer Offer Price</th>
                            </tr>
                          </thead>
                          <tbody>
                            {demand.demandItems.map((di) => (
                              <tr key={di.id}>
                                <td><strong>{di.riceVarietyName}</strong></td>
                                <td>{di.grade}</td>
                                <td>{safeNum(di.quantityTonnes).toFixed(1)} MT</td>
                                <td>
                                  <strong className="price-tag">
                                    {di.offerPricePerKg ? formatCurrency(di.offerPricePerKg) + ' / kg' : 'Market rate'}
                                  </strong>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      )}

      {/* =================================================== */}
      {/* ADMIN/GOV/OFFICER TAB: National Supply vs Demand Gaps*/}
      {/* =================================================== */}
      {isAdminOrGov && activeTab === 'gaps' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>National Demand vs Supply Equilibrium & Regional Gaps</h3>
              <p>
                Macro-level supply and demand balancing across Sri Lanka's 25 agrarian districts.
                Identifies regional surpluses for procurement distribution and deficits requiring buffer stock intervention.
              </p>
            </div>
          </div>

          {/* KPI Cards Strip */}
          <div className="marketplace-kpi-grid">
            <div className="kpi-card kpi-blue">
              <span className="kpi-title">TOTAL REGISTERED DEMAND</span>
              <div className="kpi-value">
                {safeNum(supplyOverview?.totalOpenDemandMt).toFixed(1)} <small>MT</small>
              </div>
              <span className="kpi-subtext">Commercial procurement orders</span>
            </div>

            <div className="kpi-card kpi-green">
              <span className="kpi-title">TOTAL FORECASTED HARVEST</span>
              <div className="kpi-value">
                {safeNum(supplyOverview?.totalForecastedSupplyMt).toFixed(1)} <small>MT</small>
              </div>
              <span className="kpi-subtext">Verified GPS field yields</span>
            </div>

            <div className="kpi-card kpi-purple">
              <span className="kpi-title">STORED PADDY STOCK</span>
              <div className="kpi-value">
                {safeNum(supplyOverview?.totalAvailableStockMt).toFixed(1)} <small>MT</small>
              </div>
              <span className="kpi-subtext">Ready inventory in warehouses</span>
            </div>

            <div className="kpi-card kpi-orange">
              <span className="kpi-title">ACTIVE TRADE CONTRACTS</span>
              <div className="kpi-value">
                {safeNum(supplyOverview?.activePurchaseRequestsCount)}
              </div>
              <span className="kpi-subtext">Purchase requests in pipeline</span>
            </div>
          </div>

          {/* District Supply Gap Breakdown */}
          <div className="district-gaps-section">
            <h4>District Balance & Deficit Matrix</h4>
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>DISTRICT</th>
                    <th>PROVINCE</th>
                    <th>DEMAND (MT)</th>
                    <th>FORECAST SUPPLY (MT)</th>
                    <th>STORED STOCK (MT)</th>
                    <th>NET BALANCE (MT)</th>
                    <th>EQUILIBRIUM STATUS</th>
                  </tr>
                </thead>
                <tbody>
                  {(supplyOverview?.regionalGaps || []).map((gap) => {
                    const isSurplus = gap.gapStatus === 'Surplus'
                    const isDeficit = gap.gapStatus === 'Deficit'

                    return (
                      <tr key={gap.district}>
                        <td><strong className="table-primary">{gap.district}</strong></td>
                        <td>{gap.province || '—'}</td>
                        <td>{safeNum(gap.totalDemandMt).toFixed(1)} MT</td>
                        <td>{safeNum(gap.forecastedSupplyMt).toFixed(1)} MT</td>
                        <td>{safeNum(gap.availableStockMt).toFixed(1)} MT</td>
                        <td>
                          <strong
                            className={
                              isSurplus
                                ? 'variance-positive'
                                : isDeficit
                                ? 'variance-negative'
                                : ''
                            }
                          >
                            {gap.supplyGapMt >= 0 ? `+${safeNum(gap.supplyGapMt).toFixed(1)}` : safeNum(gap.supplyGapMt).toFixed(1)} MT
                          </strong>
                        </td>
                        <td>
                          <span
                            className={`status-badge ${
                              isSurplus
                                ? 'status-positive'
                                : isDeficit
                                ? 'status-danger'
                                : 'status-neutral'
                            }`}
                          >
                            {gap.gapStatus}
                          </span>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      )}

      {/* =================================================== */}
      {/* ADMIN/GOV/OFFICER TAB: All Market Demands           */}
      {/* =================================================== */}
      {isAdminOrGov && activeTab === 'all-demands' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>All Registered Market Demands</h3>
              <p>Full administrative oversight of buyer demands recorded across the Swarnabhoomi network.</p>
            </div>
          </div>

          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>BUYER / BUSINESS</th>
                  <th>DISTRICT</th>
                  <th>SEASON</th>
                  <th>WINDOW</th>
                  <th>TOTAL DEMAND</th>
                  <th>ITEMS</th>
                  <th>STATUS</th>
                </tr>
              </thead>
              <tbody>
                {demands.map((demand) => {
                  const badge = getDemandStatusBadge(demand.status)
                  return (
                    <tr key={demand.id}>
                      <td>
                        <strong className="table-primary">{demand.ownerBusinessName || demand.ownerName}</strong>
                        <span className="table-secondary">{demand.ownerName}</span>
                      </td>
                      <td>{demand.district}</td>
                      <td>{demand.season}</td>
                      <td>{formatDate(demand.periodStart)} — {formatDate(demand.periodEnd)}</td>
                      <td><strong>{safeNum(demand.totalQuantityTonnes).toFixed(1)} MT</strong></td>
                      <td>{demand.itemsCount} varieties</td>
                      <td>
                        <span className={`status-badge ${badge.className}`}>
                          {badge.label}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* =================================================== */}
      {/* ADMIN/GOV/OFFICER TAB: All Trade Orders             */}
      {/* =================================================== */}
      {isAdminOrGov && activeTab === 'all-orders' && (
        <section className="extension-card">
          <div className="extension-toolbar">
            <div>
              <h3>All Purchase Orders & Agreements</h3>
              <p>Audit trail of all buyer-to-farmer purchase requests and trade fulfillment records.</p>
            </div>
          </div>

          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>ORDER ID</th>
                  <th>BUYER</th>
                  <th>FARMER / PRODUCER</th>
                  <th>VARIETY & DISTRICT</th>
                  <th>QUANTITY</th>
                  <th>OFFER PRICE & VALUE</th>
                  <th>STATUS</th>
                  <th>CREATED</th>
                </tr>
              </thead>
              <tbody>
                {purchaseRequests.map((pr) => {
                  const badge = getPrStatusBadge(pr.status)
                  return (
                    <tr key={pr.id}>
                      <td><strong>{pr.id.substring(0, 8).toUpperCase()}</strong></td>
                      <td>{pr.buyerBusinessName || pr.buyerName}</td>
                      <td>{pr.farmerName}</td>
                      <td>{pr.riceVarietyName} ({pr.district})</td>
                      <td>{safeNum(pr.quantityKg).toLocaleString()} kg ({safeNum(pr.quantityMt).toFixed(2)} MT)</td>
                      <td>{formatCurrency(pr.offerPricePerKg)} / kg ({formatCurrency(pr.totalOrderValueLkr)})</td>
                      <td>
                        <span className={`status-badge ${badge.className}`}>
                          {badge.label}
                        </span>
                      </td>
                      <td>{formatDate(pr.createdAt)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* =================================================== */}
      {/* MODAL: Request Purchase / Order From Supply (Buyer) */}
      {/* =================================================== */}
      {prModalOpen && prSelectedSupply && (
        <div className="marketplace-modal-overlay">
          <div className="marketplace-modal-content">
            <div className="modal-header">
              <h3>Initiate Purchase Order</h3>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setPrModalOpen(false)}
              >
                ✕
              </button>
            </div>

            <div className="modal-info-strip">
              <div>
                <span>Selected Variety:</span>
                <strong>{prSelectedSupply.riceVarietyName} ({prSelectedSupply.varietyCode})</strong>
              </div>
              <div>
                <span>District:</span>
                <strong>{prSelectedSupply.district}</strong>
              </div>
              <div>
                <span>Available Ready Stock:</span>
                <strong>{safeNum(prSelectedSupply.availableStockMt).toFixed(1)} MT</strong>
              </div>
              {prSelectedSupply.latestCbslPricePerKg && (
                <div>
                  <span>CBSL Benchmark:</span>
                  <strong>{formatCurrency(prSelectedSupply.latestCbslPricePerKg)} / kg</strong>
                </div>
              )}
            </div>

            <form onSubmit={handleSubmitPurchaseRequest} className="extension-form">
              <label>
                Link to Your Open Demand Item *
                {buyerActiveDemandItems.length > 0 ? (
                  <select
                    value={prDemandItemId}
                    onChange={(e) => setPrDemandItemId(e.target.value)}
                    required
                  >
                    <option value="">-- Choose Demand Item --</option>
                    {buyerActiveDemandItems.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.season} · {item.district} — {item.riceVarietyName} ({item.quantityTonnes} MT target)
                      </option>
                    ))}
                  </select>
                ) : (
                  <div className="demand-required-warning">
                    ⚠️ You do not have an active Demand Item for this variety yet.
                    Please post a demand first so the trade order is linked to a registered procurement demand.
                  </div>
                )}
              </label>

              <div className="form-row-2col">
                <label>
                  Purchase Quantity (kg) *
                  <input
                    type="number"
                    min="1"
                    step="1"
                    value={prQuantityKg}
                    onChange={(e) => setPrQuantityKg(e.target.value)}
                    required
                  />
                  <small>
                    = {safeNum(safeNum(prQuantityKg) / 1000).toFixed(2)} Metric Tonnes
                  </small>
                </label>

                <label>
                  Offer Price (LKR / kg) *
                  <input
                    type="number"
                    min="1"
                    step="0.5"
                    value={prOfferPrice}
                    onChange={(e) => setPrOfferPrice(e.target.value)}
                    required
                  />
                  <small>
                    Est. Total: {formatCurrency(safeNum(prQuantityKg) * safeNum(prOfferPrice))}
                  </small>
                </label>
              </div>

              <label>
                Commercial Payment Terms
                <input
                  type="text"
                  value={prTerms}
                  onChange={(e) => setPrTerms(e.target.value)}
                  placeholder="e.g. Bank transfer upon weightbridge receipt"
                />
              </label>

              <label>
                Delivery & Transport Terms
                <input
                  type="text"
                  value={prDeliveryTerms}
                  onChange={(e) => setPrDeliveryTerms(e.target.value)}
                  placeholder="e.g. Farm gate pickup by buyer"
                />
              </label>

              {/* Privacy Notice inside Modal */}
              <div className="modal-privacy-callout">
                <strong>🛡️ Privacy Protection Protocol:</strong>
                <p>
                  To prevent unauthorized solicitation and protect Sri Lankan farmers, producer contact details
                  (name, telephone, email) will remain masked until the producer explicitly accepts your order.
                </p>
              </div>

              <div className="form-action-strip">
                <button
                  type="submit"
                  className="extension-primary-button"
                  disabled={busy || !prDemandItemId}
                >
                  {busy ? 'Submitting Order…' : 'Submit Purchase Request'}
                </button>
                <button
                  type="button"
                  className="extension-secondary-button"
                  onClick={() => setPrModalOpen(false)}
                >
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
