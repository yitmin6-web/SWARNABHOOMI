import { Component, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ErrorInfo, FormEvent, ReactNode } from 'react'
import './App.css'
import './FeaturePages.css'
import AssistantConfigPage from './AssistantConfigPage'
import AssistantPage from './AssistantPage'
import CultivationPage from './CultivationPage'
import RiceIntelligencePage from './RiceIntelligencePage'
import MarketplacePage from './MarketplacePage'
import FieldMapPage from './FieldMapPage'
import MarketPricesPage from './MarketPricesPage'
import MemberDetailsPage from './MemberDetailsPage'
import WeatherPage from './WeatherPage'
import {
  SRI_LANKA_PROVINCES,
  getDistrictsForProvince,
  getProvinceForDistrict,
} from './sriLankaLocations'

type Section = 'overview' | 'farms' | 'cultivation' | 'intelligence' | 'demands' | 'prices' | 'weather' | 'varieties' | 'users' | 'field-map' | 'assistant' | 'assistant-settings' | 'member-details'
type DataSectionId = 'farms' | 'demands' | 'varieties' | 'users'

type DashboardSummary = {
  users: number | null
  farms: number | null
  demandEntries: number | null
  varieties: number | null
  marketPrices: number | null
  weatherRecords: number | null
  myFarms?: number | null
  myFields?: number | null
  myTotalAreaHectares?: number | null
  visibleCards?: string[]
  districtSupplies?: { district: string; verifiedFields: number; verifiedAreaHectares: number }[] | null
  pendingFieldVerifications?: number | null
  verifiedFields?: number | null
  forecastedHarvestTonnes?: number | null
  activeCultivations?: number | null
  openPurchaseRequests?: number | null
  unreadNotifications?: number | null
}

type NotificationItem = {
  id: string
  userId: string
  type: string
  title: string
  message: string
  category?: string | null
  link?: string | null
  isRead: boolean
  readAt?: string | null
  createdAt: string
}

type OfficerAssignmentItem = {
  id: string
  userId: string
  officerName: string
  officerEmail: string
  district: string
  province?: string | null
  assignedBy?: string | null
  assignedDate?: string | null
  isActive: boolean
  createdAt: string
}

type OfficerUserItem = {
  id: string
  name: string
  email: string
  district?: string | null
  province?: string | null
}

type User = {
  id: string
  email: string
  firstName: string
  lastName: string
  phoneNumber: string
  role: number
  isActive: boolean
  isVerified: boolean
  district: string | null
  province: string | null
  createdAt: string
}

type AuthUser = {
  id: string
  email: string
  name: string
  role: string
}

type Farm = {
  id: string
  name: string
  district: string | null
  province: string | null
  areaHectares: number
  address: string | null
  farmOwner: string
  createdAt: string
}

type Demand = {
  id: string
  ownerId: string
  district: string
  season: string
  status: string
  periodStart: string
  periodEnd: string
  notes: string | null
  createdAt: string
}

type RiceVariety = {
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
  boundaryGeoJson: string | null
  latitude: number | null
  longitude: number | null
  district: string | null
  province: string | null
  ownerName: string
  farmerName?: string | null
  verificationStatus: string
  verifierNotes?: string | null
  verifiedAt?: string | null
  isAssignedDistrict?: boolean
}

type AppData = {
  summary: DashboardSummary
  users: User[]
  farms: Farm[]
  demands: Demand[]
  varieties: RiceVariety[]
  fields: FieldMapItem[]
}

const navigation: { id: Section; label: string; icon: string }[] = [
  { id: 'overview', label: 'Overview', icon: 'grid' },
  { id: 'farms', label: 'Farms & fields', icon: 'farm' },
  { id: 'cultivation', label: 'Cultivation', icon: 'calendar' },
  { id: 'intelligence', label: 'Rice intelligence', icon: 'chart' },
  { id: 'demands', label: 'Marketplace & Demands', icon: 'layers' },
  { id: 'prices', label: 'Market prices', icon: 'chart' },
  { id: 'weather', label: 'Weather', icon: 'sun' },
  { id: 'varieties', label: 'Rice varieties', icon: 'leaf' },
  { id: 'field-map', label: 'Paddy field map', icon: 'map' },
  { id: 'assistant', label: 'AI assistant', icon: 'assistant' },
]

const sectionTitles: Record<Section, { title: string; subtitle: string }> = {
  overview: { title: 'Overview', subtitle: 'A clear view of your rice ecosystem.' },
  farms: { title: 'Farms & fields', subtitle: 'Explore registered farms across Sri Lanka.' },
  cultivation: { title: 'Cultivation cycles', subtitle: 'Seasonal crop planning, sowing dates, and growth stages.' },
  intelligence: { title: 'Rice intelligence', subtitle: 'Automated agronomic inputs, harvest yield forecasts, and supply balance.' },
  demands: { title: 'Rice Marketplace & Procurement', subtitle: 'Manage buyer demands, verified supply matching, and privacy-protected purchase orders.' },
  prices: { title: 'Market prices', subtitle: 'The latest official daily rice prices for selected markets.' },
  weather: { title: 'Weather', subtitle: 'Current model conditions across all 25 Sri Lankan districts.' },
  varieties: { title: 'Rice varieties', subtitle: 'Browse active varieties and their growing profiles.' },
  users: { title: 'People', subtitle: 'Members and roles in the Swarnabhoomi network.' },
  'member-details': { title: 'Member details', subtitle: 'A read-only view of the member profile and related records.' },
  'field-map': { title: 'Paddy field map', subtitle: 'Explore fields and their verification status on the map.' },
  assistant: { title: 'AI assistant', subtitle: 'Ask general questions about rice cultivation and farm planning.' },
  'assistant-settings': { title: 'AI assistant settings', subtitle: 'Manage the OpenAI-compatible provider used by the assistant.' },
}

const roleNames = ['Farmer', 'Buyer', 'Government', 'Officer', 'Super Administrator', 'Administrator']
const now = new Date()
const greeting = now.getHours() < 12 ? 'Good morning' : now.getHours() < 17 ? 'Good afternoon' : 'Good evening'
const todayLabel = new Intl.DateTimeFormat('en', { weekday: 'short', day: 'numeric', month: 'short' }).format(now)

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) {
    throw new Error(`Request failed (${response.status}) for ${path}`)
  }
  return response.json() as Promise<T>
}

function getErrorMessage(error: unknown) {
  if (error instanceof TypeError) {
    return 'Cannot connect to the Swarnabhoomi API. Confirm the API is running, then try again.'
  }
  return error instanceof Error ? error.message : 'Unable to load dashboard data.'
}

function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const paths: Record<string, ReactNode> = {
    grid: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
    farm: <><path d="M3 10.5 12 4l9 6.5" /><path d="M5.5 9v11h13V9M9 20v-6h6v6" /><path d="M17 5.5V3h2v4" /></>,
    layers: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5M3 16l9 5 9-5" /></>,
    chart: <><path d="M4 19V5M4 19h17" /><path d="m7 15 4-4 3 2 5-6" /><path d="M16 7h3v3" /></>,
    sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2m0 16v2M4.93 4.93l1.42 1.42m11.3 11.3 1.42 1.42M2 12h2m16 0h2M4.93 19.07l1.42-1.42m11.3-11.3 1.42-1.42" /></>,
    leaf: <><path d="M20 4c-8 0-13 3.5-13 9a7 7 0 0 0 7 7c5.5 0 9-5 9-13V4h-3Z" /><path d="M4 21c2-5 6-8 12-11" /></>,
    map: <><path d="m3 6 6-3 6 3 6-3v15l-6 3-6-3-6 3V6Z" /><path d="M9 3v15m6-12v15" /></>,
    assistant: <><path d="M12 3v2m0 14v2M3 12h2m14 0h2M5.64 5.64l1.42 1.42m9.88 9.88 1.42 1.42m0-12.72-1.42 1.42m-9.88 9.88-1.42 1.42" /><circle cx="12" cy="12" r="5" /></>,
    settings: <><circle cx="12" cy="12" r="3" /><path d="m19.4 15 .1.1 1.4 1.1-1.4 2.4-1.7-.7a8 8 0 0 1-1.4.8l-.3 1.8h-2.8l-.3-1.8a8 8 0 0 1-1.4-.8l-1.7.7-1.4-2.4 1.4-1.1a7 7 0 0 1 0-1.6l-1.4-1.1 1.4-2.4 1.7.7a8 8 0 0 1 1.4-.8l.3-1.8h2.8l.3 1.8a8 8 0 0 1 1.4.8l1.7-.7 1.4 2.4-1.4 1.1a7 7 0 0 1-.1 1.6Z" /></>,
    users: <><path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="10" cy="7" r="4" /><path d="M20 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8" /><path d="m16 16 4.5 4.5" /></>,
    bell: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" /><path d="M10 21h4" /></>,
    logout: <><path d="M10 17l5-5-5-5" /><path d="M15 12H3" /><path d="M12 3h7a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-7" /></>,
    refresh: <><path d="M20 7v5h-5" /><path d="M4 17v-5h5" /><path d="M5.7 9A7 7 0 0 1 18 6l2 2M4 16l2 2a7 7 0 0 0 12.3-3" /></>,
    arrow: <><path d="M5 12h14M13 6l6 6-6 6" /></>,
    pin: <><path d="M20 10c0 5-8 12-8 12S4 15 4 10a8 8 0 1 1 16 0Z" /><circle cx="12" cy="10" r="2.5" /></>,
    calendar: <><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M16 3v4M8 3v4M3 11h18" /></>,
    cloud: <><path d="M20 16.5a4.5 4.5 0 0 0-1.5-8.74A6 6 0 0 0 7 9a4 4 0 0 0 0 8h13Z" /><path d="m9 20 1-2m4 2 1-2" /></>,
    mail: <><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m4 7 8 6 8-6" /></>,
    lock: <><rect x="4" y="10" width="16" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3m-4 5v2" /></>,
  }

  return (
    <svg
      aria-hidden="true"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {paths[name] ?? paths.grid}
    </svg>
  )
}

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short', year: 'numeric' }).format(date)
}

function safeNum(val: unknown): number {
  if (typeof val === 'number' && !Number.isNaN(val)) return val
  const n = Number(val)
  return Number.isNaN(n) ? 0 : n
}

function initials(name: string) {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('')
}

interface ErrorBoundaryProps {
  children: ReactNode
  onReset?: () => void
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
}

class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('ErrorBoundary caught rendering error:', error, errorInfo)
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null })
    if (this.props.onReset) {
      this.props.onReset()
    }
  }

  render() {
    if (this.state.hasError) {
      return (
        <section className="extension-card error-boundary-card" style={{ padding: '24px', margin: '16px 0', borderColor: '#fecaca', background: '#fff1f2' }} role="alert">
          <div className="section-header-compact">
            <div>
              <span className="panel-eyebrow" style={{ color: '#b91c1c' }}>APPLICATION VIEW ERROR</span>
              <h3 style={{ margin: '4px 0 8px', color: '#881337' }}>Something went wrong rendering this view</h3>
              <p className="form-subtext" style={{ color: '#9f1239' }}>
                {this.state.error?.message || 'An unexpected rendering error occurred in this view.'}
              </p>
            </div>
            <button
              type="button"
              className="extension-secondary-button"
              onClick={this.handleReset}
              style={{ alignSelf: 'flex-start' }}
            >
              Reload view
            </button>
          </div>
        </section>
      )
    }

    return this.props.children
  }
}

function App() {
  const [section, setSection] = useState<Section>('overview')
  const [selectedMemberId, setSelectedMemberId] = useState<string | null>(null)
  const [data, setData] = useState<AppData | null>(null)
  const [authUser, setAuthUser] = useState<AuthUser | null>(null)
  const [authChecking, setAuthChecking] = useState(true)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const [notifications, setNotifications] = useState<NotificationItem[]>([])
  const [unreadCount, setUnreadCount] = useState<number>(0)
  const [notificationsOpen, setNotificationsOpen] = useState(false)
  const notificationRef = useRef<HTMLDivElement | null>(null)

  const fetchNotifications = useCallback(async () => {
    try {
      const res = await getJson<{ unreadCount: number; items?: NotificationItem[]; notifications?: NotificationItem[] }>('/api/notifications')
      setNotifications(res.items ?? res.notifications ?? [])
      setUnreadCount(safeNum(res.unreadCount))
    } catch {
      // Non-blocking notification fetch
    }
  }, [])

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (notificationRef.current && !notificationRef.current.contains(event.target as Node)) {
        setNotificationsOpen(false)
      }
    }
    if (notificationsOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [notificationsOpen])

  const markNotificationRead = async (id: string, link?: string | null) => {
    try {
      await fetch(`/api/notifications/${id}/read`, { method: 'PUT', credentials: 'include' })
      setNotifications((prev) => prev.map((n) => (n.id === id ? { ...n, isRead: true } : n)))
      setUnreadCount((prev) => Math.max(0, prev - 1))
    } catch {
      // Non-blocking
    }
    if (link) {
      const cleanLink = link.replace(/^\//, '').replace(/^api\//, '')
      if (['field-map', 'cultivation', 'intelligence', 'demands', 'prices', 'weather', 'overview'].includes(cleanLink)) {
        setActiveSection(cleanLink as Section)
      }
    }
    setNotificationsOpen(false)
  }

  const markAllNotificationsRead = async () => {
    try {
      await fetch('/api/notifications/read-all', { method: 'PUT', credentials: 'include' })
      setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })))
      setUnreadCount(0)
    } catch {
      // Non-blocking
    }
  }

  const loadData = useCallback(async (includeUsers: boolean, canReviewFields: boolean) => {
    const [summary, users, farms, demands, varieties, mapData] = await Promise.all([
      getJson<DashboardSummary>('/api/dashboard'),
      includeUsers ? getJson<User[]>('/api/users') : Promise.resolve([]),
      getJson<Farm[]>('/api/farms'),
      getJson<Demand[]>('/api/demands'),
      getJson<RiceVariety[]>('/api/rice-varieties'),
      canReviewFields
        ? getJson<{ fields: FieldMapItem[] }>('/api/field-map').catch(() => ({ fields: [] }))
        : Promise.resolve({ fields: [] }),
    ])
    return { summary, users, farms, demands, varieties, fields: mapData.fields || [] }
  }, [])

  useEffect(() => {
    let active = true
    const restoreSession = async () => {
      try {
        const response = await fetch('/api/auth/me', { credentials: 'include' })
        if (response.status === 401) return
        if (!response.ok) throw new Error(`Session check failed (${response.status}).`)

        const user = await response.json() as AuthUser
        if (active) setAuthUser(user)
        const isReviewer = ['SuperAdmin', 'Developer', 'Officer', 'Government'].includes(user.role)
        const loadedData = await loadData(
          user.role === 'SuperAdmin' || user.role === 'Developer',
          isReviewer,
        )
        if (active) {
          setData(loadedData)
          void fetchNotifications()
        }
      } catch (requestError) {
        if (active) setError(getErrorMessage(requestError))
      } finally {
        if (active) {
          setLoading(false)
          setAuthChecking(false)
        }
      }
    }

    void restoreSession()
    return () => {
      active = false
    }
  }, [loadData])

  const signIn = async (email: string, password: string, rememberMe: boolean) => {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, rememberMe }),
    })

    if (!response.ok) {
      if (response.status === 401) throw new Error('The email or password is incorrect.')
      if (response.status === 429) throw new Error('Too many sign-in attempts. Please wait 15 minutes and try again.')
      throw new Error(`Sign-in failed (${response.status}). Please try again.`)
    }

    const user = await response.json() as AuthUser
    setAuthUser(user)
    setError('')
    setLoading(true)
    try {
      const isReviewer = ['SuperAdmin', 'Developer', 'Officer', 'Government'].includes(user.role)
      setData(await loadData(user.role === 'SuperAdmin' || user.role === 'Developer', isReviewer))
      void fetchNotifications()
    } catch (requestError) {
      setError(getErrorMessage(requestError))
    } finally {
      setLoading(false)
    }
  }

  const register = async (
    details: {
      email: string
      password: string
      firstName: string
      lastName: string
      phoneNumber: string
      role: number
      district?: string
      province?: string
    },
  ) => {
    const response = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(details),
    })
    if (!response.ok) {
      if (response.status === 409) throw new Error('An account with this email already exists.')
      if (response.status === 429) throw new Error('Too many registration attempts. Please try again later.')
      const body = await response.json().catch(() => null) as { error?: string } | null
      throw new Error(body?.error || `Registration failed (${response.status}).`)
    }
  }

  const manageUser = async (path: string, method: 'POST' | 'PUT', body: object) => {
    const response = await fetch(path, {
      method,
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (!response.ok) {
      throw new Error(`Account update failed (${response.status}). Refresh the page and try again.`)
    }
    refreshData()
  }

  const verifyField = async (id: string, approved: boolean, notes?: string) => {
    const response = await fetch(`/api/field-map/fields/${id}/verify`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved, notes: notes || '' }),
    })
    if (!response.ok) {
      const body = await response.json().catch(() => null) as { error?: string } | null
      throw new Error(body?.error || `Field verification failed (${response.status}).`)
    }
    refreshData()
  }

  const signOut = async () => {
    try {
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'include',
      })
      if (!response.ok) throw new Error(`Sign-out failed (${response.status}).`)
      setAuthUser(null)
      setData(null)
      setError('')
      setSection('overview')
      setSelectedMemberId(null)
    } catch (requestError) {
      setError(getErrorMessage(requestError))
    }
  }

  const refreshData = () => {
    setLoading(true)
    setError('')
    void fetchNotifications()
    const isReviewer = authUser ? ['SuperAdmin', 'Developer', 'Officer', 'Government'].includes(authUser.role) : false
    void loadData(authUser?.role === 'SuperAdmin' || authUser?.role === 'Developer', isReviewer)
      .then(setData)
      .catch((requestError: unknown) => setError(getErrorMessage(requestError)))
      .finally(() => setLoading(false))
  }

  const currentTitle = sectionTitles[section]
  const isExtensionSection = ['field-map', 'cultivation', 'intelligence', 'demands', 'assistant', 'assistant-settings', 'member-details', 'prices', 'weather'].includes(section)
  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase()
    if (!query || !data) return data
    return {
      ...data,
      users: data.users.filter((item) => `${item.firstName} ${item.lastName} ${item.email} ${item.role}`.toLowerCase().includes(query)),
      farms: data.farms.filter((item) => `${item.name} ${item.district} ${item.province} ${item.farmOwner}`.toLowerCase().includes(query)),
      demands: data.demands.filter((item) => `${item.district} ${item.season} ${item.status}`.toLowerCase().includes(query)),
      varieties: data.varieties.filter((item) => `${item.name} ${item.varietyCode} ${item.description}`.toLowerCase().includes(query)),
      fields: data.fields.filter((item) => `${item.name} ${item.farmName} ${item.farmerName || item.ownerName} ${item.district} ${item.province} ${item.verificationStatus}`.toLowerCase().includes(query)),
    }
  }, [data, search])

  const setActiveSection = (next: Section) => {
    setSection(next)
    setSearch('')
    setMobileNavOpen(false)
  }
  const openMemberDetails = (memberId: string) => {
    setSelectedMemberId(memberId)
    setActiveSection('member-details')
  }

  if (authChecking) return <LoadingScreen />
  if (!authUser) return <LoginPage onLogin={signIn} onRegister={register} serviceError={error} />

  const canManageUsers = authUser.role === 'SuperAdmin' || authUser.role === 'Developer'
  const isSuperAdmin = authUser.role === 'SuperAdmin'
  const canViewFieldMap = ['Farmer', 'Buyer', 'Government', 'Officer', 'SuperAdmin'].includes(authUser.role)
  const canViewCultivation = authUser.role !== 'Buyer'

  return (
    <div className="app-shell">
      <aside className={`sidebar ${mobileNavOpen ? 'sidebar-open' : ''}`}>
        <a className="brand" href="#" onClick={(event) => { event.preventDefault(); setActiveSection('overview') }}>
          <span className="brand-mark"><Icon name="leaf" size={23} /></span>
          <span className="brand-name">swarnabhoomi<span>.</span><small>RICE NETWORK</small></span>
        </a>

        <div className="workspace-picker">
          <span className="workspace-icon">SL</span>
          <span><strong>Sri Lanka</strong><small>National workspace</small></span>
          <span className="workspace-chevron">⌄</span>
        </div>

        <div className="nav-label">WORKSPACE</div>
        <nav className="side-nav" aria-label="Main navigation">
          {navigation.map((item) => (
            (item.id !== 'field-map' || canViewFieldMap) &&
            (item.id !== 'cultivation' || canViewCultivation) && (
            <button
              className={`nav-item ${section === item.id ? 'nav-item-active' : ''}`}
              key={item.id}
              onClick={() => setActiveSection(item.id)}
            >
              <Icon name={item.icon} size={19} />
              <span>{item.label}</span>
              {item.id === 'demands' && data && <span className="nav-count">{data.summary.demandEntries}</span>}
            </button>
            )
          ))}
          {canManageUsers && (
            <button className={`nav-item ${section === 'users' || section === 'member-details' ? 'nav-item-active' : ''}`} onClick={() => setActiveSection('users')}>
              <Icon name="users" size={19} /><span>People</span>
            </button>
          )}
          {isSuperAdmin && (
            <button className={`nav-item ${section === 'assistant-settings' ? 'nav-item-active' : ''}`} onClick={() => setActiveSection('assistant-settings')}>
              <Icon name="settings" size={19} /><span>AI settings</span>
            </button>
          )}
        </nav>

        <div className="sidebar-bottom">
          <div className="season-card">
            <div className="season-top"><span className="season-dot" /> CURRENT SEASON</div>
            <strong>Yala 2027</strong>
            <span>Planning period · Mar — Jun</span>
            <div className="season-progress"><i /></div>
            <small>Season overview <Icon name="arrow" size={14} /></small>
          </div>
          <div className="profile-block">
            <div className="avatar">{initials(authUser.name)}</div>
            <div className="profile-details"><strong>{authUser.name}</strong><small>{authUser.role}</small></div>
            <button className="sign-out-button" onClick={signOut} title="Log out">
              <Icon name="logout" size={14} /><span>Log out</span>
            </button>
          </div>
        </div>
      </aside>

      <main className="main-panel">
        <header className="topbar">
          <button className="mobile-menu" onClick={() => setMobileNavOpen((open) => !open)} aria-label="Toggle navigation">
            <span /><span /><span />
          </button>
          <div className="breadcrumb"><span>Workspace</span><b>/</b><strong>{currentTitle.title}</strong></div>
          <div className="topbar-actions">
            {!isExtensionSection && <label className="global-search">
              <Icon name="search" size={17} />
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search this view..."
                aria-label="Search current view"
              />
              <kbd>⌘ K</kbd>
            </label>}
            <span className="topbar-divider" />
            <div className="notification-bell-container" ref={notificationRef}>
              <button
                type="button"
                className="icon-button notification-button"
                aria-label="Notifications"
                onClick={() => {
                  setNotificationsOpen((prev) => !prev)
                  if (!notificationsOpen) void fetchNotifications()
                }}
              >
                <Icon name="bell" size={19} />
                {unreadCount > 0 && (
                  <span className="notification-unread-pill">
                    {unreadCount > 99 ? '99+' : unreadCount}
                  </span>
                )}
              </button>
              {notificationsOpen && (
                <div className="notification-popover">
                  <div className="notification-popover-header">
                    <strong>Notifications ({unreadCount} unread)</strong>
                    <div className="notification-popover-actions">
                      {unreadCount > 0 && (
                        <button
                          type="button"
                          className="text-action-link"
                          onClick={() => void markAllNotificationsRead()}
                        >
                          Mark all as read
                        </button>
                      )}
                      <button
                        type="button"
                        className="popover-close-btn"
                        onClick={() => setNotificationsOpen(false)}
                        aria-label="Close notifications"
                      >
                        ✕
                      </button>
                    </div>
                  </div>
                  <div className="notification-list">
                    {notifications.length === 0 ? (
                      <div className="notification-empty">
                        <Icon name="bell" size={24} />
                        <span>No notifications yet</span>
                      </div>
                    ) : (
                      notifications.map((item) => (
                        <div
                          key={item.id}
                          className={`notification-item ${!item.isRead ? 'unread' : ''}`}
                          onClick={() => void markNotificationRead(item.id, item.link)}
                          role="button"
                          tabIndex={0}
                        >
                          {!item.isRead && <span className="notification-dot" />}
                          <div className="notification-item-content">
                            <div className="notification-item-top">
                              {item.category && (
                                <span className="notification-category-badge">{item.category}</span>
                              )}
                              <span className="notification-time">{formatDate(item.createdAt)}</span>
                            </div>
                            <strong className="notification-title">{item.title}</strong>
                            <p className="notification-message">{item.message}</p>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              )}
            </div>
            <span className="topbar-date">{todayLabel}</span>
          </div>
        </header>

        <div className="page-content">
          {error && (
            <div className="error-banner" role="alert">
              <span><strong>Couldn’t load live data.</strong> {error} Check that the backend is running.</span>
              <button onClick={refreshData}>Try again</button>
            </div>
          )}

          <div className="page-heading">
            <div>
              <div className="eyebrow"><span className="eyebrow-line" /> SWARNABHOOMI NETWORK</div>
              <h1>
                {section === 'overview' ? (
                  canManageUsers ? (
                    <>{greeting}, <em>Admin</em></>
                  ) : (
                    <>{greeting}, <em>{authUser.name ? authUser.name.trim().split(' ')[0] : authUser.role}</em></>
                  )
                ) : (
                  currentTitle.title
                )}
              </h1>
              <p>
                {section === 'overview'
                  ? authUser.role === 'Farmer'
                    ? 'Here’s what’s happening on your farms and fields today.'
                    : authUser.role === 'Buyer'
                    ? 'Monitor buyer demands, verified supply summaries, and official market prices.'
                    : 'Here’s what’s happening across your rice ecosystem today.'
                  : currentTitle.subtitle}
              </p>
            </div>
            {!isExtensionSection && <div className="heading-actions">
              <span className="live-status"><i /> {loading ? 'Updating' : 'Live data'}</span>
              <button className="refresh-button" onClick={refreshData} disabled={loading}>
                <Icon name="refresh" size={16} /> Refresh
              </button>
            </div>}
          </div>

          <ErrorBoundary key={section} onReset={refreshData}>
            {section === 'overview' ? (
              <Overview
                data={filtered}
                loading={loading}
                authUser={authUser}
                canManageUsers={canManageUsers}
                onNavigate={setActiveSection}
                onVerifyField={verifyField}
              />
            ) : section === 'prices' ? (
              <MarketPricesPage />
            ) : section === 'weather' ? (
              <WeatherPage />
            ) : section === 'cultivation' ? (
              canViewCultivation ? <CultivationPage role={authUser.role} /> : <AccessDenied />
            ) : section === 'intelligence' ? (
              <RiceIntelligencePage role={authUser.role} />
            ) : section === 'field-map' ? (
              canViewFieldMap ? <FieldMapPage role={authUser.role} /> : <AccessDenied />
            ) : section === 'demands' ? (
              <MarketplacePage role={authUser.role} />
            ) : section === 'assistant' ? (
              <AssistantPage />
            ) : section === 'assistant-settings' ? (
              isSuperAdmin ? <AssistantConfigPage /> : <AccessDenied />
            ) : section === 'member-details' ? (
              isSuperAdmin && selectedMemberId
                ? <MemberDetailsPage key={selectedMemberId} memberId={selectedMemberId} onBack={() => setActiveSection('users')} />
                : <AccessDenied />
            ) : (
              <DataSection
                section={section}
                data={filtered}
                loading={loading}
                onNavigate={setActiveSection}
                canManageUsers={canManageUsers}
                isSuperAdmin={isSuperAdmin}
                onViewMember={openMemberDetails}
                onReviewRegistration={(id, approved) => manageUser(`/api/users/${id}/approval`, 'POST', { approved })}
                onChangeRole={(id, role) => manageUser(`/api/users/${id}/role`, 'PUT', { role })}
                onVerifyField={verifyField}
              />
            )}
          </ErrorBoundary>

          <footer className="page-footer">
            <span>© 2027 Swarnabhoomi · Growing together</span>
            <span><i /> All systems operational</span>
          </footer>
        </div>
      </main>
    </div>
  )
}

function LoadingScreen() {
  return (
    <main className="auth-loading" aria-label="Checking your session">
      <span className="auth-loading-mark"><Icon name="leaf" size={23} /></span>
      <span className="loader" />
      <p>Preparing your workspace…</p>
    </main>
  )
}

function AccessDenied() {
  return (
    <section className="extension-card" role="alert">
      <h2>Access restricted</h2>
      <p>Your account does not have permission to view this area.</p>
    </section>
  )
}

function LoginPage({
  onLogin,
  onRegister,
  serviceError,
}: {
  onLogin: (email: string, password: string, rememberMe: boolean) => Promise<void>
  onRegister: (details: {
    email: string
    password: string
    firstName: string
    lastName: string
    phoneNumber: string
    role: number
    district?: string
    province?: string
  }) => Promise<void>
  serviceError: string
}) {
  const formRef = useRef<HTMLElement | null>(null)
  const emailRef = useRef<HTMLInputElement | null>(null)
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [province, setProvince] = useState('')
  const [district, setDistrict] = useState('')
  const [role, setRole] = useState(0)
  const [rememberMe, setRememberMe] = useState(true)
  const [showPassword, setShowPassword] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [registrationNotice, setRegistrationNotice] = useState('')

  const focusLogin = () => {
    formRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    emailRef.current?.focus({ preventScroll: true })
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError('')
    setRegistrationNotice('')
    setSubmitting(true)
    try {
      if (mode === 'login') {
        await onLogin(email.trim(), password, rememberMe)
      } else {
        await onRegister({
          email: email.trim(),
          password,
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          phoneNumber: '',
          role,
          district: district.trim(),
          province: province.trim(),
        })
        setMode('login')
        setPassword('')
        setRegistrationNotice('Your request was sent. You can sign in after an administrator approves your account.')
      }
    } catch (submitError) {
      setError(getErrorMessage(submitError))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="login-shell">
      <header className="login-topbar">
        <a className="login-brand" href="/" aria-label="Swarnabhoomi home">
          <span className="brand-mark"><Icon name="leaf" size={23} /></span>
          <span className="brand-name">swarnabhoomi<span>.</span><small>RICE NETWORK</small></span>
        </a>
        <div className="login-top-action">
          <span>{mode === 'login' ? 'New to Swarnabhoomi?' : 'Already a member?'}</span>
          <button
            type="button"
            className="login-top-button"
            onClick={() => {
              setMode(mode === 'login' ? 'register' : 'login')
              setError('')
              setRegistrationNotice('')
              focusLogin()
            }}
          >
            {mode === 'login' ? 'Sign up' : 'Sign in'} <Icon name="arrow" size={15} />
          </button>
        </div>
      </header>

      <main className="login-layout">
        <section className="login-left">
          <div className="login-intro">
            <div className="login-eyebrow"><span /> A CONNECTED RICE COMMUNITY</div>
            <h1>Rooted here.<br /><em>Growing together.</em></h1>
            <p>Sign in to stay close to your farms, your market and the people growing Sri Lanka’s next harvest.</p>
          </div>

          <section className="login-form-panel" ref={formRef} aria-labelledby="login-title">
            <span className="form-kicker">{mode === 'login' ? 'YOUR WORKSPACE AWAITS' : 'JOIN THE RICE NETWORK'}</span>
            <h2 id="login-title">{mode === 'login' ? 'Welcome back' : 'Create your account'}</h2>
            <p className="form-description">
              {mode === 'login' ? 'Use your Swarnabhoomi account to continue.' : 'Your account will be ready after administrator approval.'}
            </p>
            {registrationNotice && <div className="registration-success" role="status">{registrationNotice}</div>}
            {(error || serviceError) && (
              <div className="login-error" role="alert">
                <strong>{error ? (mode === 'login' ? 'Sign-in unsuccessful.' : 'Sign-up unsuccessful.') : 'Workspace unavailable.'}</strong>
                <span>{error || serviceError}</span>
              </div>
            )}
            <form onSubmit={submit}>
              {mode === 'register' && (
                <>
                  <div className="registration-name-fields">
                    <label className="login-field">
                      <span>First name</span>
                      <span className="login-input-wrap">
                        <input value={firstName} onChange={(event) => setFirstName(event.target.value)} maxLength={100} autoComplete="given-name" required />
                      </span>
                    </label>
                    <label className="login-field">
                      <span>Last name</span>
                      <span className="login-input-wrap">
                        <input value={lastName} onChange={(event) => setLastName(event.target.value)} maxLength={100} autoComplete="family-name" required />
                      </span>
                    </label>
                  </div>
                  <div className="registration-name-fields">
                    <label className="login-field">
                      <span>Province</span>
                      <span className="login-input-wrap">
                        <input
                          list="registration-provinces"
                          value={province}
                          onChange={(event) => {
                            const val = event.target.value
                            setProvince(val)
                            const valid = getDistrictsForProvince(val)
                            if (district && !valid.includes(district)) {
                              setDistrict('')
                            }
                          }}
                          placeholder="Select or enter province"
                          maxLength={100}
                        />
                        <datalist id="registration-provinces">
                          {SRI_LANKA_PROVINCES.map((p) => (
                            <option key={p} value={p} />
                          ))}
                        </datalist>
                      </span>
                    </label>
                    <label className="login-field">
                      <span>District</span>
                      <span className="login-input-wrap">
                        <input
                          list="registration-districts"
                          value={district}
                          onChange={(event) => {
                            const val = event.target.value
                            setDistrict(val)
                            if (!province) {
                              const matched = getProvinceForDistrict(val)
                              if (matched) setProvince(matched)
                            }
                          }}
                          placeholder="Select or enter district"
                          maxLength={100}
                        />
                        <datalist id="registration-districts">
                          {getDistrictsForProvince(province).map((d) => (
                            <option key={d} value={d} />
                          ))}
                        </datalist>
                      </span>
                    </label>
                  </div>
                </>
              )}
              <label className="login-field">
                <span>Email address</span>
                <span className="login-input-wrap">
                  <Icon name="mail" size={17} />
                  <input
                    ref={emailRef}
                    type="email"
                    name="email"
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                    placeholder="you@example.com"
                    autoComplete={mode === 'login' ? 'username' : 'email'}
                    maxLength={254}
                    required
                  />
                </span>
              </label>
              {mode === 'register' && (
                <label className="login-field">
                  <span>Account type</span>
                  <span className="login-input-wrap">
                    <select value={role} onChange={(event) => setRole(Number(event.target.value))}>
                      <option value={0}>Farmer</option>
                      <option value={1}>Buyer</option>
                      <option value={2}>Government</option>
                    </select>
                  </span>
                </label>
              )}
              <div className="login-field">
                <label htmlFor="login-password">{mode === 'login' ? 'Password' : 'Create a password'}</label>
                <span className="login-input-wrap">
                  <Icon name="lock" size={17} />
                  <input
                    id="login-password"
                    type={showPassword ? 'text' : 'password'}
                    name="password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    placeholder="Enter your password"
                    autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                    minLength={mode === 'register' ? 12 : undefined}
                    maxLength={256}
                    required
                  />
                  <button
                    type="button"
                    className="password-toggle"
                    onClick={() => setShowPassword((visible) => !visible)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                  >
                    {showPassword ? 'Hide' : 'Show'}
                  </button>
                </span>
              </div>
              {mode === 'login' ? (
                <div className="login-options">
                  <label className="remember-option">
                    <input
                      type="checkbox"
                      checked={rememberMe}
                      onChange={(event) => setRememberMe(event.target.checked)}
                    />
                    <span className="custom-checkbox" />
                    Keep me signed in
                  </label>
                  <span className="secure-note"><Icon name="lock" size={13} /> Secure sign-in</span>
                </div>
              ) : (
                <p className="registration-password-note">Use at least 12 characters. Administrator approval is required before sign-in.</p>
              )}
              <button className="login-submit" type="submit" disabled={submitting}>
                {submitting
                  ? <><span className="button-spinner" /> {mode === 'login' ? 'Signing in…' : 'Sending request…'}</>
                  : <>{mode === 'login' ? 'Sign in to your account' : 'Request an account'} <Icon name="arrow" size={17} /></>}
              </button>
            </form>
            <p className="login-help">
              {mode === 'login' ? (
                <>Need an account? <button type="button" onClick={() => { setMode('register'); setError(''); setRegistrationNotice('') }}>Sign up for approval</button></>
              ) : (
                <>Already registered? <button type="button" onClick={() => { setMode('login'); setError('') }}>Sign in</button></>
              )}
            </p>
          </section>
        </section>

        <aside className="login-visual" aria-label="Swarnabhoomi rice fields">
          <div className="visual-topline"><span>GROWN WITH PURPOSE</span><span>EST. SRI LANKA</span></div>
          <div className="visual-sun" />
          <div className="visual-mountain visual-mountain-back" />
          <div className="visual-mountain visual-mountain-front" />
          <div className="visual-field visual-field-back" />
          <div className="visual-field visual-field-mid" />
          <div className="visual-field visual-field-front" />
          <div className="visual-rice visual-rice-one"><i /><i /><i /><b /></div>
          <div className="visual-rice visual-rice-two"><i /><i /><i /><b /></div>
          <div className="visual-caption">
            <span>ONE NETWORK. MANY HARVESTS.</span>
            <h2>Every season,<br /><em>we grow stronger.</em></h2>
            <p>Connecting farmers, buyers and communities from field to future.</p>
          </div>
          <div className="visual-bottom"><span>06°55′ N&nbsp; 79°51′ E</span><span>WESTERN PROVINCE · SRI LANKA</span></div>
        </aside>
      </main>
      <footer className="login-footer">
        <span>© 2027 Swarnabhoomi · Growing together</span>
        <span><i /> A better harvest starts with connection</span>
      </footer>
    </div>
  )
}

function FarmerOverview({
  data,
  loading,
  onNavigate,
}: {
  data: AppData | null
  loading: boolean
  onNavigate: (section: Section) => void
}) {
  const myFarmsCount = data?.summary.myFarms ?? data?.farms.length ?? 0
  const myFieldsCount = data?.summary.myFields ?? 0
  const myTotalArea = (
    data?.summary.myTotalAreaHectares ??
    data?.farms.reduce((area, farm) => area + farm.areaHectares, 0) ??
    0
  ).toFixed(1)
  const activeCultivations = safeNum(data?.summary.activeCultivations)
  const forecastedHarvest = safeNum(data?.summary.forecastedHarvestTonnes)

  const farmerMetrics = [
    {
      title: 'My Farms',
      value: myFarmsCount,
      foot: 'Registered to your account',
      icon: 'farm',
      color: 'blue',
      section: 'farms' as Section,
    },
    {
      title: 'My Mapped Fields',
      value: myFieldsCount,
      foot: `${myTotalArea} ha mapped footprint`,
      icon: 'map',
      color: 'green',
      section: 'field-map' as Section,
    },
    {
      title: 'Active Cultivations',
      value: activeCultivations,
      foot: 'Current cycle plantings',
      icon: 'leaf',
      color: 'purple',
      section: 'cultivation' as Section,
    },
    {
      title: 'Forecasted Yield',
      value: `${forecastedHarvest.toFixed(1)} MT`,
      foot: 'Projected paddy output',
      icon: 'chart',
      color: 'orange',
      section: 'intelligence' as Section,
    },
  ]

  return (
    <>
      <section className="welcome-banner">
        <div className="welcome-copy">
          <span className="banner-kicker"><span /> FARMER WORKSPACE</span>
          <h2>Field insight.<br /><em>Stronger harvests.</em></h2>
          <p>One connected view for your registered farms, GPS field boundaries, and regional rice prices.</p>
          <button className="banner-link" onClick={() => onNavigate('field-map')}>
            Open Satellite Map <Icon name="arrow" size={16} />
          </button>
        </div>
        <div className="banner-art" aria-hidden="true">
          <div className="sun-disc" />
          <div className="field field-back" />
          <div className="field field-middle" />
          <div className="field field-front" />
          <div className="plant plant-one"><i /><i /><i /><b /></div>
          <div className="plant plant-two"><i /><i /><i /><b /></div>
          <span className="art-label art-label-top">YALA SEASON <b>’27</b></span>
          <span className="art-label art-label-bottom">ROOTED IN PROGRESS</span>
        </div>
      </section>

      <section className="metric-grid" aria-label="Farmer summary">
        {farmerMetrics.map((metric, index) => (
          <button
            className="metric-card"
            key={metric.title}
            onClick={() => onNavigate(metric.section)}
          >
            <div className="metric-top">
              <span className={`metric-icon ${metric.color}`}>
                <Icon name={metric.icon} size={18} />
              </span>
              <span className="metric-index">0{index + 1}</span>
            </div>
            <div className="metric-value">
              {loading ? <span className="skeleton skeleton-number" /> : metric.value}
            </div>
            <div className="metric-title">{metric.title}</div>
            <div className="metric-foot">
              {metric.foot}
              <span className="metric-arrow"><Icon name="arrow" size={15} /></span>
            </div>
          </button>
        ))}
      </section>

      <div className="dashboard-grid">
        <section className="panel recent-panel">
          <div className="panel-header">
            <div>
              <span className="panel-eyebrow">YOUR HOLDINGS</span>
              <h3>My registered farms</h3>
            </div>
            <button className="text-link" onClick={() => onNavigate('field-map')}>
              Open map <Icon name="arrow" size={15} />
            </button>
          </div>
          {data?.farms.length ? (
            <div className="farm-list">
              {data.farms.map((farm) => (
                <div className="farm-row" key={farm.id}>
                  <div className="farm-avatar"><Icon name="leaf" size={19} /></div>
                  <div className="farm-info">
                    <strong>{farm.name}</strong>
                    <span><Icon name="pin" size={13} /> {farm.district || 'Location not set'}, {farm.province || 'Sri Lanka'}</span>
                  </div>
                  <div className="farm-area">
                    <strong>{farm.areaHectares.toLocaleString()} ha</strong>
                    <span>Your farm</span>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState loading={loading} label="No farms registered yet. Open the satellite map to register your first farm." />
          )}
          <div className="panel-note"><span className="note-dot" /> Farm plots and boundaries are strictly private to you</div>
        </section>

        <section className="panel market-panel">
          <div className="panel-header">
            <div><span className="panel-eyebrow">MARKET PULSE</span><h3>Latest price watch</h3></div>
            <button className="round-link" aria-label="View all market prices" onClick={() => onNavigate('prices')}><Icon name="arrow" size={17} /></button>
          </div>
          <div className="overview-live-card">
            <span className="rice-stamp"><Icon name="chart" size={18} /></span>
            <div><strong>Official daily rice prices</strong><small>Latest report from the Central Bank of Sri Lanka</small></div>
            <button type="button" className="text-link" onClick={() => onNavigate('prices')}>View report <Icon name="arrow" size={14} /></button>
          </div>
        </section>
      </div>

      <div className="bottom-grid">
        <section className="season-strip">
          <div className="season-icon-large"><Icon name="calendar" size={21} /></div>
          <div><span className="panel-eyebrow">SEASON AT A GLANCE</span><h3>Yala 2027 planning is underway</h3><p>Manage your cultivation cycles, sowing dates, and growth stages.</p></div>
          <button className="text-link" onClick={() => onNavigate('cultivation')}>Manage cultivation <Icon name="arrow" size={15} /></button>
        </section>
        <section className="weather-card">
          <div className="weather-heading"><span className="panel-eyebrow">LOCAL WEATHER</span><Icon name="sun" size={19} /></div>
          <div className="weather-main"><strong>25</strong><span>districts</span></div>
          <div className="weather-facts"><span><Icon name="cloud" size={15} /> Real-time forecast model</span></div>
          <button type="button" className="text-link" onClick={() => onNavigate('weather')}>View district weather <Icon name="arrow" size={14} /></button>
        </section>
      </div>
    </>
  )
}

function BuyerOverview({
  data,
  loading,
  onNavigate,
}: {
  data: AppData | null
  loading: boolean
  onNavigate: (section: Section) => void
}) {
  const buyerMetrics = [
    { title: 'Demand entries', value: data?.summary.demandEntries ?? '—', foot: 'Procurement demand', icon: 'layers', color: 'orange', section: 'demands' as Section },
    { title: 'Open Purchase Requests', value: safeNum(data?.summary.openPurchaseRequests), foot: 'Active farmer deals', icon: 'layers', color: 'blue', section: 'demands' as Section },
    { title: 'National Forecasted Supply', value: `${safeNum(data?.summary.forecastedHarvestTonnes).toFixed(1)} MT`, foot: 'Projected paddy yield', icon: 'leaf', color: 'purple', section: 'intelligence' as Section },
    { title: 'Official Price Watch', value: 'CBSL', foot: 'Daily market report', icon: 'chart', color: 'green', section: 'prices' as Section },
  ]

  const supplies = data?.summary.districtSupplies || []

  return (
    <>
      <section className="welcome-banner">
        <div className="welcome-copy">
          <span className="banner-kicker"><span /> BUYER WORKSPACE</span>
          <h2>Market demand.<br /><em>Verified harvests.</em></h2>
          <p>Monitor procurement demands, check CBSL daily rates, and review verified regional supply.</p>
          <button className="banner-link" onClick={() => onNavigate('demands')}>Explore Demands <Icon name="arrow" size={16} /></button>
        </div>
        <div className="banner-art" aria-hidden="true">
          <div className="sun-disc" />
          <div className="field field-back" />
          <div className="field field-middle" />
          <div className="field field-front" />
          <div className="plant plant-one"><i /><i /><i /><b /></div>
          <div className="plant plant-two"><i /><i /><i /><b /></div>
          <span className="art-label art-label-top">YALA SEASON <b>’27</b></span>
          <span className="art-label art-label-bottom">ROOTED IN PROGRESS</span>
        </div>
      </section>

      <section className="metric-grid" aria-label="Buyer summary">
        {buyerMetrics.map((metric, index) => (
          <button
            className="metric-card"
            key={metric.title}
            onClick={() => onNavigate(metric.section)}
          >
            <div className="metric-top">
              <span className={`metric-icon ${metric.color}`}><Icon name={metric.icon} size={18} /></span>
              <span className="metric-index">0{index + 1}</span>
            </div>
            <div className="metric-value">{loading ? <span className="skeleton skeleton-number" /> : metric.value}</div>
            <div className="metric-title">{metric.title}</div>
            <div className="metric-foot">{metric.foot}<span className="metric-arrow"><Icon name="arrow" size={15} /></span></div>
          </button>
        ))}
      </section>

      <div className="dashboard-grid">
        <section className="panel recent-panel">
          <div className="panel-header">
            <div><span className="panel-eyebrow">SUPPLY INTELLIGENCE</span><h3>Verified supply by district</h3></div>
            <button className="text-link" onClick={() => onNavigate('field-map')}>View on map <Icon name="arrow" size={15} /></button>
          </div>
          {supplies.length ? (
            <div className="farm-list">
              {supplies.slice(0, 4).map((supply) => (
                <div className="farm-row" key={supply.district}>
                  <div className="farm-avatar"><Icon name="map" size={19} /></div>
                  <div className="farm-info"><strong>{supply.district}</strong><span><Icon name="pin" size={13} /> {supply.verifiedFields} verified fields</span></div>
                  <div className="farm-area"><strong>{supply.verifiedAreaHectares.toFixed(1)} ha</strong><span>Verified supply</span></div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState loading={loading} label="District supply data is updating." />
          )}
          <div className="panel-note"><span className="note-dot" /> Aggregated district metrics protect individual farmer privacy</div>
        </section>

        <section className="panel market-panel">
          <div className="panel-header">
            <div><span className="panel-eyebrow">MARKET PULSE</span><h3>Latest price watch</h3></div>
            <button className="round-link" aria-label="View all market prices" onClick={() => onNavigate('prices')}><Icon name="arrow" size={17} /></button>
          </div>
          <div className="overview-live-card">
            <span className="rice-stamp"><Icon name="chart" size={18} /></span>
            <div><strong>Official daily rice prices</strong><small>Latest report from the Central Bank of Sri Lanka</small></div>
            <button type="button" className="text-link" onClick={() => onNavigate('prices')}>View report <Icon name="arrow" size={14} /></button>
          </div>
        </section>
      </div>

      <div className="bottom-grid">
        <section className="season-strip">
          <div className="season-icon-large"><Icon name="calendar" size={21} /></div>
          <div><span className="panel-eyebrow">SEASON AT A GLANCE</span><h3>Yala 2027 procurement window</h3><p>Manage requirements and connect with verified regional supplies.</p></div>
          <button className="text-link" onClick={() => onNavigate('demands')}>View demand <Icon name="arrow" size={15} /></button>
        </section>
        <section className="weather-card">
          <div className="weather-heading"><span className="panel-eyebrow">FIELD CONDITIONS</span><Icon name="sun" size={19} /></div>
          <div className="weather-main"><strong>25</strong><span>district centers</span></div>
          <div className="weather-facts"><span><Icon name="cloud" size={15} /> Current model conditions</span></div>
          <button type="button" className="text-link" onClick={() => onNavigate('weather')}>View district weather <Icon name="arrow" size={14} /></button>
        </section>
      </div>
    </>
  )
}

function FieldVerificationSection({
  fields,
  onVerify,
  refreshing,
}: {
  fields: FieldMapItem[]
  onVerify: (fieldId: string, approved: boolean, notes?: string) => Promise<void>
  refreshing: boolean
}) {
  const [remarks, setRemarks] = useState<Record<string, string>>({})
  const [busyId, setBusyId] = useState<string | null>(null)
  const [actionError, setActionError] = useState('')

  const handleAction = async (fieldId: string, approved: boolean) => {
    setBusyId(fieldId)
    setActionError('')
    try {
      await onVerify(fieldId, approved, remarks[fieldId])
      setRemarks((prev) => {
        const next = { ...prev }
        delete next[fieldId]
        return next
      })
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to update field verification.')
    } finally {
      setBusyId(null)
    }
  }

  const sortedFields = useMemo(() => {
    return [...fields].sort((a, b) => {
      const aPending = (a.verificationStatus || 'Pending') === 'Pending'
      const bPending = (b.verificationStatus || 'Pending') === 'Pending'
      if (aPending && !bPending) return -1
      if (!aPending && bPending) return 1
      if (aPending && bPending) {
        if (a.isAssignedDistrict && !b.isAssignedDistrict) return -1
        if (!a.isAssignedDistrict && b.isAssignedDistrict) return 1
      }
      return 0
    })
  }, [fields])

  const pendingCount = fields.filter((f) => (f.verificationStatus || 'Pending') === 'Pending').length

  return (
    <section className="pending-approvals" aria-labelledby="field-verifications-title" style={{ marginTop: '24px' }}>
      <div className="pending-approvals-heading">
        <div>
          <span className="panel-eyebrow">FIELD VERIFICATION QUEUE</span>
          <h4 id="field-verifications-title">Field Verifications</h4>
        </div>
        <span className="record-count">{pendingCount} pending</span>
      </div>

      {actionError && <p className="user-action-error" role="alert">{actionError}</p>}

      {fields.length === 0 ? (
        <EmptyState loading={refreshing} label="No mapped fields have been submitted yet." />
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>FIELD</th>
                <th>FARM & OWNER</th>
                <th>LOCATION</th>
                <th>AUTO AREA</th>
                <th>STATUS</th>
                <th>REMARK / NOTES</th>
                <th>ACTION</th>
              </tr>
            </thead>
            <tbody>
              {sortedFields.map((field) => {
                const status = field.verificationStatus || 'Pending'
                const isVerified = status === 'Verified'
                const isRejected = status === 'Rejected'
                const isPending = !isVerified && !isRejected

                return (
                  <tr key={field.id}>
                    <td>
                      <strong className="table-primary">{field.name}</strong>
                      {field.isAssignedDistrict && (
                        <span className="assigned-priority-badge">★ Assigned Priority</span>
                      )}
                    </td>
                    <td>
                      <strong className="table-primary">{field.farmName}</strong>
                      <span className="table-secondary">{field.farmerName || field.ownerName || 'Unknown farmer'}</span>
                    </td>
                    <td>
                      {field.district || '—'}
                      <span className="table-secondary">{field.province || 'Sri Lanka'}</span>
                    </td>
                    <td>
                      <strong>{field.areaHectares.toFixed(2)} ha</strong>
                    </td>
                    <td>
                      <span
                        className={`status-badge ${
                          isVerified
                            ? 'status-positive'
                            : isRejected
                            ? 'status-neutral'
                            : 'status-pending'
                        }`}
                        style={isRejected ? { color: '#991b1b', background: '#fee2e2' } : undefined}
                      >
                        <i style={isRejected ? { background: '#ef4444' } : undefined} />
                        {status}
                      </span>
                    </td>
                    <td>
                      {isPending ? (
                        <input
                          type="text"
                          aria-label={`Remark for ${field.name}`}
                          placeholder="Optional notes or reason..."
                          value={remarks[field.id] || ''}
                          onChange={(e) =>
                            setRemarks((prev) => ({ ...prev, [field.id]: e.target.value }))
                          }
                          style={{
                            padding: '4px 8px',
                            fontSize: '12px',
                            border: '1px solid #dce5da',
                            borderRadius: '6px',
                            width: '100%',
                            minWidth: '130px',
                          }}
                        />
                      ) : (
                        <span className="table-secondary">{field.verifierNotes || '—'}</span>
                      )}
                    </td>
                    <td>
                      <div className="user-action-buttons">
                        <button
                          type="button"
                          className="user-approve-button"
                          disabled={busyId === field.id || refreshing || isVerified}
                          onClick={() => void handleAction(field.id, true)}
                        >
                          Verify
                        </button>
                        <button
                          type="button"
                          className="user-reject-button"
                          disabled={busyId === field.id || refreshing || isRejected}
                          onClick={() => void handleAction(field.id, false)}
                        >
                          Reject
                        </button>
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
  )
}

function OfficerAssignmentSection({
  canManage,
}: {
  canManage: boolean
}) {
  const [assignments, setAssignments] = useState<OfficerAssignmentItem[]>([])
  const [assignableOfficers, setAssignableOfficers] = useState<OfficerUserItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedOfficerId, setSelectedOfficerId] = useState('')
  const [province, setProvince] = useState('')
  const [district, setDistrict] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const fetchAssignments = useCallback(async () => {
    try {
      setLoading(true)
      const res = await getJson<{
        assignments: OfficerAssignmentItem[]
        assignableOfficers: OfficerUserItem[]
      }>('/api/officer-assignments')
      setAssignments(res.assignments || [])
      setAssignableOfficers(res.assignableOfficers || [])
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void fetchAssignments()
  }, [fetchAssignments])

  const handleAssign = async (e: FormEvent) => {
    e.preventDefault()
    if (!selectedOfficerId || !district) return
    setSubmitting(true)
    setError('')
    try {
      const res = await fetch('/api/officer-assignments', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId: selectedOfficerId,
          district: district.trim(),
          province: province.trim() || undefined,
        }),
      })
      if (!res.ok) {
        const body = await res.json().catch(() => null) as { error?: string } | null
        throw new Error(body?.error || `Failed to assign district (${res.status}).`)
      }
      setSelectedOfficerId('')
      setDistrict('')
      setProvince('')
      await fetchAssignments()
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleRemove = async (id: string) => {
    setDeletingId(id)
    setError('')
    try {
      const res = await fetch(`/api/officer-assignments/${id}`, {
        method: 'DELETE',
        credentials: 'include',
      })
      if (!res.ok) {
        const body = await res.json().catch(() => null) as { error?: string } | null
        throw new Error(body?.error || `Failed to remove assignment (${res.status}).`)
      }
      await fetchAssignments()
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      setDeletingId(null)
    }
  }

  const availableDistricts = getDistrictsForProvince(province)

  return (
    <section className="pending-approvals" aria-labelledby="officer-assignments-title" style={{ marginTop: '24px' }}>
      <div className="pending-approvals-heading">
        <div>
          <span className="panel-eyebrow">GOVERNANCE & JURISDICTION</span>
          <h4 id="officer-assignments-title">
            {canManage ? 'Officer District Assignments' : 'My Assigned Jurisdictions'}
          </h4>
        </div>
        <span className="record-count">{assignments.length} active assignments</span>
      </div>

      {error && <p className="user-action-error" role="alert">{error}</p>}

      {canManage && (
        <form className="officer-assign-form" onSubmit={handleAssign}>
          <div className="officer-form-row">
            <label>
              <span>Agricultural Officer</span>
              <select
                value={selectedOfficerId}
                onChange={(e) => setSelectedOfficerId(e.target.value)}
                required
              >
                <option value="">Select an officer...</option>
                {assignableOfficers.map((officer) => (
                  <option key={officer.id} value={officer.id}>
                    {officer.name} ({officer.email})
                  </option>
                ))}
              </select>
            </label>
            <label>
              <span>Province</span>
              <input
                list="assign-provinces"
                value={province}
                onChange={(e) => {
                  const val = e.target.value
                  setProvince(val)
                  const valid = getDistrictsForProvince(val)
                  if (district && !valid.includes(district)) {
                    setDistrict('')
                  }
                }}
                placeholder="Select or enter province"
              />
              <datalist id="assign-provinces">
                {SRI_LANKA_PROVINCES.map((p) => (
                  <option key={p} value={p} />
                ))}
              </datalist>
            </label>
            <label>
              <span>District *</span>
              <input
                list="assign-districts"
                value={district}
                onChange={(e) => {
                  const val = e.target.value
                  setDistrict(val)
                  if (!province) {
                    const matched = getProvinceForDistrict(val)
                    if (matched) setProvince(matched)
                  }
                }}
                placeholder="Select or enter district"
                required
              />
              <datalist id="assign-districts">
                {availableDistricts.map((d) => (
                  <option key={d} value={d} />
                ))}
              </datalist>
            </label>
            <button
              type="submit"
              className="user-approve-button"
              disabled={submitting || !selectedOfficerId || !district}
              style={{ minHeight: '38px', padding: '0 16px' }}
            >
              {submitting ? 'Assigning...' : 'Assign District'}
            </button>
          </div>
        </form>
      )}

      {assignments.length === 0 ? (
        <EmptyState
          loading={loading}
          label={canManage ? "No officer district assignments yet. Assign an officer above." : "You have not been assigned to any specific districts yet. You can still review all fields."}
        />
      ) : (
        <div className="table-scroll" style={{ marginTop: '12px' }}>
          <table>
            <thead>
              <tr>
                <th>OFFICER</th>
                <th>DISTRICT</th>
                <th>PROVINCE</th>
                <th>ASSIGNED BY</th>
                <th>ASSIGNED DATE</th>
                {canManage && <th>ACTION</th>}
              </tr>
            </thead>
            <tbody>
              {assignments.map((assignment) => (
                <tr key={assignment.id}>
                  <td>
                    <strong className="table-primary">{assignment.officerName}</strong>
                    <span className="table-secondary">{assignment.officerEmail}</span>
                  </td>
                  <td>
                    <span className="status-badge status-positive">
                      <i /> {assignment.district}
                    </span>
                  </td>
                  <td>{assignment.province || '—'}</td>
                  <td><span className="table-secondary">{assignment.assignedBy || 'Admin'}</span></td>
                  <td>{formatDate(assignment.assignedDate || assignment.createdAt)}</td>
                  {canManage && (
                    <td>
                      <button
                        type="button"
                        className="user-reject-button"
                        disabled={deletingId === assignment.id}
                        onClick={() => void handleRemove(assignment.id)}
                      >
                        {deletingId === assignment.id ? 'Removing...' : 'Unassign'}
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function AdminOverview({
  data,
  loading,
  authUser,
  canManageUsers,
  onNavigate,
  onVerifyField,
}: {
  data: AppData | null
  loading: boolean
  authUser: AuthUser
  canManageUsers: boolean
  onNavigate: (section: Section) => void
  onVerifyField: (fieldId: string, approved: boolean, notes?: string) => Promise<void>
}) {
  const isOfficer = authUser.role === 'Officer'
  const canAssignOfficers = ['SuperAdmin', 'Developer', 'Government'].includes(authUser.role)
  const canViewOfficerAssignments = ['SuperAdmin', 'Developer', 'Government', 'Officer'].includes(authUser.role)

  const metrics = isOfficer
    ? [
        { title: 'Pending Verifications', value: data?.summary.pendingFieldVerifications ?? 0, foot: 'Fields awaiting review', icon: 'map', color: 'orange', section: 'field-map' as Section },
        { title: 'Verified Fields', value: data?.summary.verifiedFields ?? 0, foot: 'Approved GPS boundaries', icon: 'leaf', color: 'green', section: 'field-map' as Section },
        { title: 'Active Cultivations', value: safeNum(data?.summary.activeCultivations), foot: 'Monitored plantings', icon: 'calendar', color: 'blue', section: 'cultivation' as Section },
        { title: 'Forecasted Harvest', value: `${safeNum(data?.summary.forecastedHarvestTonnes).toFixed(1)} MT`, foot: 'Regional yield forecast', icon: 'chart', color: 'purple', section: 'intelligence' as Section },
      ]
    : [
        { title: 'Network members', value: data?.summary.users ?? '—', foot: 'Across the network', icon: 'users', color: 'green', section: (canManageUsers ? 'users' : 'overview') as Section },
        { title: 'Registered farms', value: data?.summary.farms ?? '—', foot: `${data?.farms.reduce((area, farm) => area + farm.areaHectares, 0).toFixed(1) ?? '0.0'} ha registered`, icon: 'farm', color: 'blue', section: 'farms' as Section },
        { title: 'Pending Verifications', value: data?.summary.pendingFieldVerifications ?? 0, foot: 'Fields awaiting review', icon: 'map', color: 'orange', section: 'field-map' as Section },
        { title: 'Active Cultivations', value: safeNum(data?.summary.activeCultivations), foot: `${safeNum(data?.summary.forecastedHarvestTonnes).toFixed(1)} MT forecasted`, icon: 'leaf', color: 'purple', section: 'cultivation' as Section },
      ]

  return (
    <>
      <section className="welcome-banner">
        <div className="welcome-copy">
          <span className="banner-kicker"><span /> THE HARVEST STARTS HERE</span>
          <h2>Better insights.<br /><em>Stronger harvests.</em></h2>
          <p>One connected view for the people, places and progress behind Sri Lankan rice.</p>
          <button className="banner-link" onClick={() => onNavigate('farms')}>Explore the network <Icon name="arrow" size={16} /></button>
        </div>
        <div className="banner-art" aria-hidden="true">
          <div className="sun-disc" />
          <div className="field field-back" />
          <div className="field field-middle" />
          <div className="field field-front" />
          <div className="plant plant-one"><i /><i /><i /><b /></div>
          <div className="plant plant-two"><i /><i /><i /><b /></div>
          <span className="art-label art-label-top">YALA SEASON <b>’27</b></span>
          <span className="art-label art-label-bottom">ROOTED IN PROGRESS</span>
        </div>
      </section>

      <section className="metric-grid" aria-label="Network summary">
        {metrics.map((metric, index) => (
          <button
            className="metric-card"
            key={metric.title}
            onClick={() => onNavigate(metric.section)}
            disabled={metric.section === 'overview'}
            aria-label={metric.section === 'overview' ? `${metric.title}: ${metric.value}` : undefined}
          >
            <div className="metric-top">
              <span className={`metric-icon ${metric.color}`}><Icon name={metric.icon} size={18} /></span>
              <span className="metric-index">0{index + 1}</span>
            </div>
            <div className="metric-value">{loading ? <span className="skeleton skeleton-number" /> : metric.value}</div>
            <div className="metric-title">{metric.title}</div>
            <div className="metric-foot">{metric.foot}<span className="metric-arrow"><Icon name="arrow" size={15} /></span></div>
          </button>
        ))}
      </section>

      <FieldVerificationSection
        fields={data?.fields || []}
        onVerify={onVerifyField}
        refreshing={loading}
      />

      {canViewOfficerAssignments && (
        <OfficerAssignmentSection
          canManage={canAssignOfficers}
        />
      )}

      <div className="dashboard-grid">
        <section className="panel recent-panel">
          <div className="panel-header">
            <div><span className="panel-eyebrow">ON THE GROUND</span><h3>Farms in the network</h3></div>
            <button className="text-link" onClick={() => onNavigate('farms')}>View all <Icon name="arrow" size={15} /></button>
          </div>
          {data?.farms.length ? (
            <div className="farm-list">
              {data.farms.slice(0, 3).map((farm) => (
                <div className="farm-row" key={farm.id}>
                  <div className="farm-avatar"><Icon name="leaf" size={19} /></div>
                  <div className="farm-info"><strong>{farm.name}</strong><span><Icon name="pin" size={13} /> {farm.district || 'Location not set'}, {farm.province || 'Sri Lanka'}</span></div>
                  <div className="farm-area"><strong>{farm.areaHectares.toLocaleString()} ha</strong><span>{farm.farmOwner}</span></div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState loading={loading} label="No farms have been registered yet." />
          )}
          <div className="panel-note"><span className="note-dot" /> Farm information is synced from your registry</div>
        </section>

        <section className="panel market-panel">
          <div className="panel-header">
            <div><span className="panel-eyebrow">MARKET PULSE</span><h3>Latest price watch</h3></div>
            <button className="round-link" aria-label="View all market prices" onClick={() => onNavigate('prices')}><Icon name="arrow" size={17} /></button>
          </div>
          <div className="overview-live-card">
            <span className="rice-stamp"><Icon name="chart" size={18} /></span>
            <div><strong>Official daily rice prices</strong><small>Latest report from the Central Bank of Sri Lanka</small></div>
            <button type="button" className="text-link" onClick={() => onNavigate('prices')}>View report <Icon name="arrow" size={14} /></button>
          </div>
        </section>
      </div>

      <div className="bottom-grid">
        <section className="season-strip">
          <div className="season-icon-large"><Icon name="calendar" size={21} /></div>
          <div><span className="panel-eyebrow">SEASON AT A GLANCE</span><h3>Yala 2027 planning is underway</h3><p>Demand window runs from March through June. Keep your farm plans up to date.</p></div>
          <button className="text-link" onClick={() => onNavigate('demands')}>View demand <Icon name="arrow" size={15} /></button>
        </section>
        <section className="weather-card">
          <div className="weather-heading"><span className="panel-eyebrow">FIELD CONDITIONS</span><Icon name="sun" size={19} /></div>
          <div className="weather-main"><strong>25</strong><span>district centers</span></div>
          <div className="weather-facts"><span><Icon name="cloud" size={15} /> Current model conditions</span></div>
          <button type="button" className="text-link" onClick={() => onNavigate('weather')}>View district weather <Icon name="arrow" size={14} /></button>
        </section>
      </div>
    </>
  )
}

function Overview({
  data,
  loading,
  authUser,
  canManageUsers,
  onNavigate,
  onVerifyField,
}: {
  data: AppData | null
  loading: boolean
  authUser: AuthUser
  canManageUsers: boolean
  onNavigate: (section: Section) => void
  onVerifyField: (fieldId: string, approved: boolean, notes?: string) => Promise<void>
}) {
  if (authUser.role === 'Farmer') {
    return <FarmerOverview data={data} loading={loading} onNavigate={onNavigate} />
  }
  if (authUser.role === 'Buyer') {
    return <BuyerOverview data={data} loading={loading} onNavigate={onNavigate} />
  }
  return (
    <AdminOverview
      data={data}
      loading={loading}
      authUser={authUser}
      canManageUsers={canManageUsers}
      onNavigate={onNavigate}
      onVerifyField={onVerifyField}
    />
  )
}

function DataSection({
  section,
  data,
  loading,
  onNavigate,
  canManageUsers,
  isSuperAdmin,
  onReviewRegistration,
  onChangeRole,
  onViewMember,
  onVerifyField,
}: {
  section: DataSectionId
  data: AppData | null
  loading: boolean
  onNavigate: (section: Section) => void
  canManageUsers: boolean
  isSuperAdmin: boolean
  onReviewRegistration: (id: string, approved: boolean) => Promise<void>
  onChangeRole: (id: string, role: number) => Promise<void>
  onViewMember: (id: string) => void
  onVerifyField: (fieldId: string, approved: boolean, notes?: string) => Promise<void>
}) {
  const [actionError, setActionError] = useState('')
  const [busyUserId, setBusyUserId] = useState('')
  const pendingUsers = data?.users.filter((user) => !user.isActive && !user.isVerified) ?? []

  const runUserAction = async (id: string, action: () => Promise<void>) => {
    setBusyUserId(id)
    setActionError('')
    try {
      await action()
    } catch (requestError) {
      setActionError(getErrorMessage(requestError))
    } finally {
      setBusyUserId('')
    }
  }

  const recordCount = !data ? 0 : ({
    farms: data.farms.length,
    demands: data.demands.length,
    varieties: data.varieties.length,
    users: data.users.length,
  })[section]

  return (
    <section className="panel data-panel">
      <div className="data-toolbar">
        <div><span className="panel-eyebrow">DIRECTORY</span><h3>{sectionTitles[section].title}</h3></div>
        <span className="record-count">{loading ? 'Loading…' : `${recordCount} ${recordCount === 1 ? 'record' : 'records'}`}</span>
      </div>
      {section === 'users' && canManageUsers && pendingUsers.length > 0 && (
        <section className="pending-approvals" aria-labelledby="pending-approvals-title">
          <div className="pending-approvals-heading">
            <div><span className="panel-eyebrow">REVIEW REQUIRED</span><h4 id="pending-approvals-title">Sign-up requests</h4></div>
            <span className="record-count">{pendingUsers.length} pending</span>
          </div>
          {actionError && <p className="user-action-error" role="alert">{actionError}</p>}
          <div className="table-scroll">
            <table>
              <thead><tr><th>APPLICANT</th><th>REQUESTED ROLE</th><th>SUBMITTED</th><th>ACTION</th></tr></thead>
              <tbody>{pendingUsers.map((user) => (
                <tr key={user.id}>
                  <td><strong className="table-primary">{user.firstName} {user.lastName}</strong><span className="table-secondary">{user.email}</span></td>
                  <td><span className="role-label">{roleNames[user.role] || 'Member'}</span></td>
                  <td>{formatDate(user.createdAt)}</td>
                  <td>
                    <div className="user-action-buttons">
                      {isSuperAdmin && <button type="button" className="member-detail-button" onClick={() => onViewMember(user.id)}>View details</button>}
                      <button type="button" className="user-approve-button" disabled={busyUserId === user.id} onClick={() => void runUserAction(user.id, () => onReviewRegistration(user.id, true))}>Approve</button>
                      <button type="button" className="user-reject-button" disabled={busyUserId === user.id} onClick={() => void runUserAction(user.id, () => onReviewRegistration(user.id, false))}>Reject</button>
                    </div>
                  </td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </section>
      )}
      {section === 'users' && canManageUsers && (
        <FieldVerificationSection
          fields={data?.fields || []}
          onVerify={onVerifyField}
          refreshing={loading}
        />
      )}
      {section === 'users' && actionError && pendingUsers.length === 0 && <p className="user-action-error" role="alert">{actionError}</p>}
      {loading && !data ? <div className="table-loading"><span className="loader" /> Loading live records…</div> : null}
      {!loading && recordCount === 0 ? <EmptyState loading={loading} label="Nothing to show here yet." /> : null}
      {data && recordCount > 0 && (
        <div className="table-scroll">
          <table>
            {section === 'farms' && <>
              <thead><tr><th>FARM</th><th>LOCATION</th><th>OWNER</th><th>AREA</th><th>REGISTERED</th></tr></thead>
              <tbody>{data.farms.map((farm) => <tr key={farm.id}><td><strong className="table-primary">{farm.name}</strong><span className="table-secondary">{farm.address || 'Address not provided'}</span></td><td>{farm.district || '—'}<span className="table-secondary">{farm.province || '—'}</span></td><td>{farm.farmOwner}</td><td><strong>{farm.areaHectares.toLocaleString()} ha</strong></td><td>{formatDate(farm.createdAt)}</td></tr>)}</tbody>
            </>}
            {section === 'demands' && <>
              <thead><tr><th>SEASON</th><th>DISTRICT</th><th>PERIOD</th><th>STATUS</th><th>CREATED</th></tr></thead>
              <tbody>{data.demands.map((demand) => <tr key={demand.id}><td><strong className="table-primary">{demand.season}</strong><span className="table-secondary">{demand.notes || 'No notes'}</span></td><td>{demand.district}</td><td>{formatDate(demand.periodStart)} — {formatDate(demand.periodEnd)}</td><td><StatusBadge status={demand.status} /></td><td>{formatDate(demand.createdAt)}</td></tr>)}</tbody>
            </>}
            {section === 'varieties' && <>
              <thead><tr><th>VARIETY</th><th>CODE</th><th>MATURITY</th><th>AVERAGE YIELD</th><th>STATUS</th></tr></thead>
              <tbody>{data.varieties.map((variety) => <tr key={variety.id}><td><strong className="table-primary">{variety.name}</strong><span className="table-secondary">{variety.description || 'No description'}</span></td><td><span className="code-chip">{variety.varietyCode}</span></td><td>{variety.maturityDays} days</td><td>{variety.averageYieldKgPerHectare?.toLocaleString() ?? '—'} kg/ha</td><td><StatusBadge status={variety.isActive ? 'Active' : 'Inactive'} /></td></tr>)}</tbody>
            </>}
            {section === 'users' && <>
              <thead><tr><th>MEMBER</th><th>ROLE</th><th>LOCATION</th><th>STATUS</th><th>JOINED</th>{isSuperAdmin && <th>PROFILE</th>}</tr></thead>
              <tbody>{data.users.filter((user) => user.isActive || user.isVerified).map((user) => <tr key={user.id}>
                <td><strong className="table-primary">{user.firstName} {user.lastName}</strong><span className="table-secondary">{user.email}</span></td>
                <td>{isSuperAdmin && user.role !== 4
                  ? <select className="role-select" value={user.role} disabled={busyUserId === user.id} aria-label={`Change role for ${user.email}`} onChange={(event) => void runUserAction(user.id, () => onChangeRole(user.id, Number(event.target.value)))}>
                    {roleNames.slice(0, 4).map((name, role) => <option key={name} value={role}>{name}</option>)}
                    <option value={5}>Administrator</option>
                  </select>
                  : <span className="role-label">{roleNames[user.role] || 'Member'}</span>}
                </td>
                <td>{user.district || '—'}<span className="table-secondary">{user.province || '—'}</span></td>
                <td><StatusBadge status={user.isActive ? 'Active' : 'Inactive'} /></td>
                <td>{formatDate(user.createdAt)}</td>
                {isSuperAdmin && <td><button type="button" className="member-detail-button" onClick={() => onViewMember(user.id)}>View details</button></td>}
              </tr>)}</tbody>
            </>}
          </table>
        </div>
      )}
      {!loading && recordCount === 0 && section === 'farms' && (
        <button className="empty-action" onClick={() => onNavigate('overview')}>Return to overview <Icon name="arrow" size={15} /></button>
      )}
    </section>
  )
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase()
  const className = ['active', 'submitted', 'approved', 'fulfilled'].includes(normalized)
    ? 'status-positive'
    : ['draft', 'pending'].includes(normalized)
      ? 'status-pending'
      : 'status-neutral'
  return <span className={`status-badge ${className}`}><i />{status}</span>
}

function EmptyState({ loading, label }: { loading: boolean; label: string }) {
  return <div className="empty-state"><span className="empty-icon"><Icon name="leaf" size={20} /></span><span>{loading ? 'Loading live records…' : label}</span></div>
}

export default App
