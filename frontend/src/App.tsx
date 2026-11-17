import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import './App.css'
import './FeaturePages.css'
import AssistantConfigPage from './AssistantConfigPage'
import AssistantPage from './AssistantPage'
import FieldMapPage from './FieldMapPage'
import MarketPricesPage from './MarketPricesPage'
import MemberDetailsPage from './MemberDetailsPage'
import WeatherPage from './WeatherPage'

type Section = 'overview' | 'farms' | 'demands' | 'prices' | 'weather' | 'varieties' | 'users' | 'field-map' | 'assistant' | 'assistant-settings' | 'member-details'
type DataSectionId = 'farms' | 'demands' | 'varieties' | 'users'

type DashboardSummary = {
  users: number
  farms: number
  demandEntries: number
  varieties: number
  marketPrices: number
  weatherRecords: number
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

type AppData = {
  summary: DashboardSummary
  users: User[]
  farms: Farm[]
  demands: Demand[]
  varieties: RiceVariety[]
}

const navigation: { id: Section; label: string; icon: string }[] = [
  { id: 'overview', label: 'Overview', icon: 'grid' },
  { id: 'farms', label: 'Farms & fields', icon: 'farm' },
  { id: 'demands', label: 'Demand board', icon: 'layers' },
  { id: 'prices', label: 'Market prices', icon: 'chart' },
  { id: 'weather', label: 'Weather', icon: 'sun' },
  { id: 'varieties', label: 'Rice varieties', icon: 'leaf' },
  { id: 'field-map', label: 'Paddy field map', icon: 'map' },
  { id: 'assistant', label: 'AI assistant', icon: 'assistant' },
]

const sectionTitles: Record<Section, { title: string; subtitle: string }> = {
  overview: { title: 'Overview', subtitle: 'A clear view of your rice ecosystem.' },
  farms: { title: 'Farms & fields', subtitle: 'Explore registered farms across Sri Lanka.' },
  demands: { title: 'Demand board', subtitle: 'Keep track of buyer demand and upcoming seasons.' },
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

function formatDate(value: string) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(value))
}

function initials(name: string) {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('')
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

  const loadData = useCallback(async (includeUsers: boolean) => {
    const [summary, users, farms, demands, varieties] = await Promise.all([
      getJson<DashboardSummary>('/api/dashboard'),
      includeUsers ? getJson<User[]>('/api/users') : Promise.resolve([]),
      getJson<Farm[]>('/api/farms'),
      getJson<Demand[]>('/api/demands'),
      getJson<RiceVariety[]>('/api/rice-varieties'),
    ])
    return { summary, users, farms, demands, varieties }
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
        const loadedData = await loadData(
          user.role === 'SuperAdmin' || user.role === 'Developer',
        )
        if (active) setData(loadedData)
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
      setData(await loadData(user.role === 'SuperAdmin' || user.role === 'Developer'))
    } catch (requestError) {
      setError(getErrorMessage(requestError))
    } finally {
      setLoading(false)
    }
  }

  const register = async (
    details: { email: string; password: string; firstName: string; lastName: string; phoneNumber: string; role: number },
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
    void loadData(authUser?.role === 'SuperAdmin' || authUser?.role === 'Developer')
      .then(setData)
      .catch((requestError: unknown) => setError(getErrorMessage(requestError)))
      .finally(() => setLoading(false))
  }

  const currentTitle = sectionTitles[section]
  const isExtensionSection = ['field-map', 'assistant', 'assistant-settings', 'member-details', 'prices', 'weather'].includes(section)
  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase()
    if (!query || !data) return data
    return {
      ...data,
      users: data.users.filter((item) => `${item.firstName} ${item.lastName} ${item.email} ${item.role}`.toLowerCase().includes(query)),
      farms: data.farms.filter((item) => `${item.name} ${item.district} ${item.province} ${item.farmOwner}`.toLowerCase().includes(query)),
      demands: data.demands.filter((item) => `${item.district} ${item.season} ${item.status}`.toLowerCase().includes(query)),
      varieties: data.varieties.filter((item) => `${item.name} ${item.varietyCode} ${item.description}`.toLowerCase().includes(query)),
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
            (item.id !== 'field-map' || canViewFieldMap) && (
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
            {!isExtensionSection && <button className="icon-button notification-button" aria-label="Notifications">
              <Icon name="bell" size={19} /><i />
            </button>}
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
              <h1>{section === 'overview' ? <>{greeting}, <em>Admin</em></> : currentTitle.title}</h1>
              <p>{section === 'overview' ? 'Here’s what’s happening across your rice ecosystem today.' : currentTitle.subtitle}</p>
            </div>
            {!isExtensionSection && <div className="heading-actions">
              <span className="live-status"><i /> {loading ? 'Updating' : 'Live data'}</span>
              <button className="refresh-button" onClick={refreshData} disabled={loading}>
                <Icon name="refresh" size={16} /> Refresh
              </button>
            </div>}
          </div>

          {section === 'overview' ? (
            <Overview
              data={filtered}
              loading={loading}
              canManageUsers={canManageUsers}
              onNavigate={setActiveSection}
            />
          ) : section === 'prices' ? (
            <MarketPricesPage />
          ) : section === 'weather' ? (
            <WeatherPage />
          ) : section === 'field-map' ? (
            canViewFieldMap ? <FieldMapPage role={authUser.role} /> : <AccessDenied />
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
            />
          )}

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
  onRegister: (details: { email: string; password: string; firstName: string; lastName: string; phoneNumber: string; role: number }) => Promise<void>
  serviceError: string
}) {
  const formRef = useRef<HTMLElement | null>(null)
  const emailRef = useRef<HTMLInputElement | null>(null)
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
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
        await onRegister({ email: email.trim(), password, firstName: firstName.trim(), lastName: lastName.trim(), phoneNumber: '', role })
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

function Overview({
  data,
  loading,
  canManageUsers,
  onNavigate,
}: {
  data: AppData | null
  loading: boolean
  canManageUsers: boolean
  onNavigate: (section: Section) => void
}) {
  const metrics = [
    { title: 'Network members', value: data?.summary.users ?? '—', foot: 'Across the network', icon: 'users', color: 'green', section: (canManageUsers ? 'users' : 'overview') as Section },
    { title: 'Registered farms', value: data?.summary.farms ?? '—', foot: `${data?.farms.reduce((area, farm) => area + farm.areaHectares, 0).toFixed(1) ?? '0.0'} ha registered`, icon: 'farm', color: 'blue', section: 'farms' as Section },
    { title: 'Demand entries', value: data?.summary.demandEntries ?? '—', foot: 'Buyer demand entries', icon: 'layers', color: 'orange', section: 'demands' as Section },
    { title: 'Rice varieties', value: data?.summary.varieties ?? '—', foot: 'Available to cultivate', icon: 'leaf', color: 'purple', section: 'varieties' as Section },
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
