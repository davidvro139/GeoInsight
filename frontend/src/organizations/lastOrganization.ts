const STORAGE_KEY = 'geoinsight.lastOrganizationId'

export function getLastOrganizationId(): number | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  return raw ? Number(raw) : null
}

export function setLastOrganizationId(id: number): void {
  localStorage.setItem(STORAGE_KEY, String(id))
}
