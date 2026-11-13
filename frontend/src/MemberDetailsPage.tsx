import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'

type MemberAccount = {
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
  lastLoginAt: string | null
}

type MemberProfile = {
  type: string
  address: string | null
  totalLandAreaHectares: number | null
  primaryVariety: string | null
  experienceYears: string | null
  businessName: string | null
  contactPerson: string | null
  monthlyDemandTonnes: number | null
  preferredVarieties: string | null
  institution: string | null
  department: string | null
  designation: string | null
  district: string | null
  province: string | null
}

type MemberDetails = {
  account: MemberAccount
  profile: MemberProfile | null
  officerAssignments: {
    district: string
    province: string | null
    isActive: boolean
    assignedDate: string | null
  }[]
  farms: {
    id: string
    name: string
    description: string | null
    areaHectares: number
    district: string | null
    province: string | null
    address: string | null
    createdAt: string
    fields: {
      id: string
      name: string
      areaHectares: number
      latitude: number | null
      longitude: number | null
      verificationStatus: string
    }[]
  }[]
  cultivations: {
    id: string
    fieldName: string
    farmName: string
    varietyName: string
    season: string
    plantingDate: string
    expectedHarvestDate: string | null
    areaHectares: number
    status: string
    notes: string | null
    forecasts: {
      targetWindowStart: string
      targetWindowEnd: string
      predictedPaddyQuantityKg: number
      confidenceCategory: string
    }[]
    harvests: {
      actualHarvestDate: string
      actualPaddyQuantityKg: number
      quality: string | null
      verificationStatus: string
    }[]
  }[]
  demands: {
    id: string
    district: string
    season: string
    status: string
    periodStart: string
    periodEnd: string
    notes: string | null
    createdAt: string
    items: {
      varietyName: string
      grade: string
      quantityTonnes: number
      offerPricePerKg: number | null
    }[]
  }[]
  purchaseRequests: {
    memberRole: string
    buyerName: string
    farmerName: string | null
    season: string
    varietyName: string
    grade: string
    quantityKg: number
    offerPricePerKg: number
    status: string
    createdAt: string
  }[]
}

const roleNames = ['Farmer', 'Buyer', 'Government', 'Officer', 'Super Administrator', 'Administrator']

function formatDate(value: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en', { dateStyle: 'medium' }).format(date)
}

function formatNumber(value: number | null, suffix = '') {
  return value == null ? '—' : `${value.toLocaleString()}${suffix}`
}

function DetailValue({ label, value }: { label: string; value: string | number | null }) {
  return (
    <div className="member-detail-value">
      <span>{label}</span>
      <strong>{value == null || value === '' ? '—' : value}</strong>
    </div>
  )
}

function DetailSection({
  title,
  count,
  children,
}: {
  title: string
  count: number
  children: ReactNode
}) {
  return (
    <section className="member-detail-section">
      <div className="member-detail-section-heading">
        <h3>{title}</h3>
        <span>{count}</span>
      </div>
      {children}
    </section>
  )
}

export default function MemberDetailsPage({
  memberId,
  onBack,
}: {
  memberId: string
  onBack: () => void
}) {
  const [details, setDetails] = useState<MemberDetails | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    const loadDetails = async () => {
      setLoading(true)
      setError('')
      setDetails(null)
      try {
        const response = await fetch(`/api/admin/members/${encodeURIComponent(memberId)}`, {
          credentials: 'include',
          signal: controller.signal,
        })
        if (!response.ok) {
          const message = response.status === 403
            ? 'Your account does not have permission to view member details.'
            : response.status === 404
              ? 'This member could not be found.'
              : `Member details could not be loaded (${response.status}).`
          throw new Error(message)
        }
        setDetails(await response.json() as MemberDetails)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') return
        setError(requestError instanceof Error ? requestError.message : 'Member details could not be loaded.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void loadDetails()
    return () => controller.abort()
  }, [memberId])

  return (
    <div className="extension-page member-details-page">
      <section className="extension-card">
        <div className="member-details-toolbar">
          <div>
            <span className="panel-eyebrow">SUPER ADMINISTRATOR · READ ONLY</span>
            <h2>{details ? `${details.account.firstName} ${details.account.lastName}` : 'Member details'}</h2>
            <p>Profile and related records across the Swarnabhoomi network.</p>
          </div>
          <button type="button" className="extension-secondary-button" onClick={onBack}>Back to People</button>
        </div>

        {loading && <div className="member-details-message" role="status">Loading member details…</div>}
        {error && <div className="extension-alert extension-alert-error" role="alert">{error}</div>}

        {details && (
          <div className="member-details-content">
            <DetailSection title="Account" count={1}>
              <div className="member-detail-grid">
                <DetailValue label="Email" value={details.account.email} />
                <DetailValue label="Role" value={roleNames[details.account.role] || 'Member'} />
                <DetailValue label="Phone" value={details.account.phoneNumber} />
                <DetailValue label="Status" value={details.account.isActive ? 'Active' : 'Inactive'} />
                <DetailValue label="Verified" value={details.account.isVerified ? 'Yes' : 'No'} />
                <DetailValue label="Location" value={[details.account.district, details.account.province].filter(Boolean).join(', ')} />
                <DetailValue label="Joined" value={formatDate(details.account.createdAt)} />
                <DetailValue label="Last sign-in" value={formatDate(details.account.lastLoginAt)} />
              </div>
            </DetailSection>

            {details.profile && (
              <DetailSection title={`${details.profile.type} profile`} count={1}>
                <div className="member-detail-grid">
                  {details.profile.type === 'Farmer' && <>
                    <DetailValue label="Total land area" value={formatNumber(details.profile.totalLandAreaHectares, ' ha')} />
                    <DetailValue label="Primary variety" value={details.profile.primaryVariety} />
                    <DetailValue label="Experience" value={details.profile.experienceYears ? `${details.profile.experienceYears} years` : null} />
                  </>}
                  {details.profile.type === 'Buyer' && <>
                    <DetailValue label="Business" value={details.profile.businessName} />
                    <DetailValue label="Contact person" value={details.profile.contactPerson} />
                    <DetailValue label="Monthly demand" value={formatNumber(details.profile.monthlyDemandTonnes, ' tonnes')} />
                    <DetailValue label="Preferred varieties" value={details.profile.preferredVarieties} />
                  </>}
                  {details.profile.type === 'Government' && <>
                    <DetailValue label="Institution" value={details.profile.institution} />
                    <DetailValue label="Department" value={details.profile.department} />
                    <DetailValue label="Designation" value={details.profile.designation} />
                    <DetailValue label="District" value={details.profile.district} />
                    <DetailValue label="Province" value={details.profile.province} />
                  </>}
                  {details.profile.address && <DetailValue label="Address" value={details.profile.address} />}
                </div>
              </DetailSection>
            )}

            {details.officerAssignments.length > 0 && (
              <DetailSection title="Officer assignments" count={details.officerAssignments.length}>
                <div className="member-detail-list">
                  {details.officerAssignments.map((assignment, index) => (
                    <article className="member-detail-record" key={`${assignment.district}-${index}`}>
                      <strong>{assignment.district}{assignment.province ? `, ${assignment.province}` : ''}</strong>
                      <span>{assignment.isActive ? 'Active' : 'Inactive'} · Assigned {formatDate(assignment.assignedDate)}</span>
                    </article>
                  ))}
                </div>
              </DetailSection>
            )}

            <DetailSection title="Farms and fields" count={details.farms.length}>
              {details.farms.length === 0 ? <p className="member-details-empty">No farms are linked to this member.</p> : (
                <div className="member-detail-list">
                  {details.farms.map((farm) => (
                    <article className="member-detail-record" key={farm.id}>
                      <div className="member-detail-record-heading">
                        <strong>{farm.name}</strong>
                        <span>{formatNumber(farm.areaHectares, ' ha')} · {farm.district || 'Location not set'}</span>
                      </div>
                      {farm.description && <p>{farm.description}</p>}
                      {farm.fields.length === 0 ? <span>No fields recorded</span> : (
                        <div className="member-detail-subrecords">
                          {farm.fields.map((field) => (
                            <div className="member-detail-subrecord" key={field.id}>
                              <strong>{field.name}</strong>
                              <span>{formatNumber(field.areaHectares, ' ha')} · {field.verificationStatus}</span>
                              {field.latitude != null && field.longitude != null && <small>{field.latitude}, {field.longitude}</small>}
                            </div>
                          ))}
                        </div>
                      )}
                    </article>
                  ))}
                </div>
              )}
            </DetailSection>

            <DetailSection title="Cultivations and harvests" count={details.cultivations.length}>
              {details.cultivations.length === 0 ? <p className="member-details-empty">No cultivation records are linked to this member.</p> : (
                <div className="member-detail-list">
                  {details.cultivations.map((cultivation) => (
                    <article className="member-detail-record" key={cultivation.id}>
                      <div className="member-detail-record-heading">
                        <strong>{cultivation.varietyName} · {cultivation.season}</strong>
                        <span>{cultivation.status}</span>
                      </div>
                      <p>{cultivation.farmName} / {cultivation.fieldName} · {formatNumber(cultivation.areaHectares, ' ha')}</p>
                      <span>Planted {formatDate(cultivation.plantingDate)} · Expected harvest {formatDate(cultivation.expectedHarvestDate)}</span>
                      {cultivation.forecasts.map((forecast, index) => (
                        <small key={`${forecast.targetWindowStart}-${index}`}>
                          Forecast: {formatNumber(forecast.predictedPaddyQuantityKg, ' kg')} · {forecast.confidenceCategory} · {formatDate(forecast.targetWindowStart)}–{formatDate(forecast.targetWindowEnd)}
                        </small>
                      ))}
                      {cultivation.harvests.map((harvest, index) => (
                        <small key={`${harvest.actualHarvestDate}-${index}`}>
                          Harvest: {formatNumber(harvest.actualPaddyQuantityKg, ' kg')} · {formatDate(harvest.actualHarvestDate)} · {harvest.verificationStatus}
                        </small>
                      ))}
                    </article>
                  ))}
                </div>
              )}
            </DetailSection>

            <DetailSection title="Demands" count={details.demands.length}>
              {details.demands.length === 0 ? <p className="member-details-empty">No demands are linked to this member.</p> : (
                <div className="member-detail-list">
                  {details.demands.map((demand) => (
                    <article className="member-detail-record" key={demand.id}>
                      <div className="member-detail-record-heading">
                        <strong>{demand.season} · {demand.district}</strong>
                        <span>{demand.status}</span>
                      </div>
                      <span>{formatDate(demand.periodStart)}–{formatDate(demand.periodEnd)} · Created {formatDate(demand.createdAt)}</span>
                      {demand.items.map((item, index) => (
                        <small key={`${item.varietyName}-${index}`}>
                          {item.varietyName}, {item.grade} · {formatNumber(item.quantityTonnes, ' tonnes')}
                          {item.offerPricePerKg == null ? '' : ` · ${formatNumber(item.offerPricePerKg, ' /kg')}`}
                        </small>
                      ))}
                      {demand.notes && <p>{demand.notes}</p>}
                    </article>
                  ))}
                </div>
              )}
            </DetailSection>

            <DetailSection title="Purchase requests" count={details.purchaseRequests.length}>
              {details.purchaseRequests.length === 0 ? <p className="member-details-empty">No purchase requests are linked to this member.</p> : (
                <div className="member-detail-list">
                  {details.purchaseRequests.map((request, index) => (
                    <article className="member-detail-record" key={`${request.createdAt}-${index}`}>
                      <div className="member-detail-record-heading">
                        <strong>{request.varietyName} · {request.grade}</strong>
                        <span>{request.status}</span>
                      </div>
                      <span>{request.memberRole} · {request.season} · {formatNumber(request.quantityKg, ' kg')}</span>
                      <small>Buyer: {request.buyerName} · Farmer: {request.farmerName || 'Not assigned'} · {formatDate(request.createdAt)}</small>
                    </article>
                  ))}
                </div>
              )}
            </DetailSection>
          </div>
        )}
      </section>
    </div>
  )
}
