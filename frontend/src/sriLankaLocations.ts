export const SRI_LANKA_PROVINCES = [
  'Western Province',
  'Central Province',
  'Southern Province',
  'Northern Province',
  'Eastern Province',
  'North Western Province',
  'North Central Province',
  'Uva Province',
  'Sabaragamuwa Province',
] as const

export type SriLankaProvince = (typeof SRI_LANKA_PROVINCES)[number]

export const SRI_LANKA_DISTRICTS_BY_PROVINCE: Record<SriLankaProvince, readonly string[]> = {
  'Western Province': ['Colombo', 'Gampaha', 'Kalutara'],
  'Central Province': ['Kandy', 'Matale', 'Nuwara Eliya'],
  'Southern Province': ['Galle', 'Matara', 'Hambantota'],
  'Northern Province': ['Jaffna', 'Kilinochchi', 'Mannar', 'Mullaitivu', 'Vavuniya'],
  'Eastern Province': ['Trincomalee', 'Batticaloa', 'Ampara'],
  'North Western Province': ['Kurunegala', 'Puttalam'],
  'North Central Province': ['Anuradhapura', 'Polonnaruwa'],
  'Uva Province': ['Badulla', 'Monaragala'],
  'Sabaragamuwa Province': ['Ratnapura', 'Kegalle'],
} as const

export const ALL_SRI_LANKA_DISTRICTS: readonly string[] = Object.values(
  SRI_LANKA_DISTRICTS_BY_PROVINCE,
).flat()

/**
 * Returns districts for a given province. If province is empty or invalid, returns all 25 districts.
 */
export function getDistrictsForProvince(province?: string | null): readonly string[] {
  if (!province) return ALL_SRI_LANKA_DISTRICTS
  const trimmed = province.trim()
  if (trimmed in SRI_LANKA_DISTRICTS_BY_PROVINCE) {
    return SRI_LANKA_DISTRICTS_BY_PROVINCE[trimmed as SriLankaProvince]
  }

  // Allow fuzzy match without "Province" suffix, e.g. "Western" or "North Western"
  const normalized = trimmed.toLowerCase().replace(/\s+province$/, '')
  const matched = SRI_LANKA_PROVINCES.find((p) =>
    p.toLowerCase().replace(/\s+province$/, '') === normalized,
  )
  if (matched) {
    return SRI_LANKA_DISTRICTS_BY_PROVINCE[matched]
  }

  return ALL_SRI_LANKA_DISTRICTS
}

/**
 * Given a district name, finds the corresponding Sri Lankan province, or null if not found.
 */
export function getProvinceForDistrict(district?: string | null): SriLankaProvince | null {
  if (!district) return null
  const trimmed = district.trim().toLowerCase()
  for (const [province, districts] of Object.entries(SRI_LANKA_DISTRICTS_BY_PROVINCE)) {
    if (districts.some((d) => d.toLowerCase() === trimmed)) {
      return province as SriLankaProvince
    }
  }
  return null
}
