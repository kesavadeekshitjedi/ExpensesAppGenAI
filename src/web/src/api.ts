import {
  clearTokens,
  doRefresh,
  ensureFreshAccess,
  getAccessToken,
  getRefreshToken,
  setTokens,
  type TokenBundle,
} from './auth/tokens'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

// API calls authenticate with a bearer access token (not a cookie — iOS blocks cross-site cookies).
// On a 401 we transparently refresh once and retry.
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const run = () =>
    fetch(`${apiBaseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...authHeader(),
        ...init?.headers,
      },
    })

  let res = await run()
  if (res.status === 401 && getRefreshToken()) {
    if (await doRefresh()) res = await run()
  }
  return res
}

function authHeader(): Record<string, string> {
  const token = getAccessToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

export type Me = {
  memberId: string
  householdId: string
  role: 'Parent' | 'Child'
  displayName: string
  email: string | null
}

// Trade a Microsoft ID token for an app session (bearer tokens). Returns the member, or an error message.
export async function signInWithToken(idToken: string, invitationCode: string | null): Promise<{ me?: Me; error?: string }> {
  const res = await fetch(`${apiBaseUrl}/auth/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider: 'Microsoft', token: idToken, invitationCode }),
  })
  if (res.ok) {
    const bundle = (await res.json()) as TokenBundle
    setTokens(bundle)
    return { me: bundle.me }
  }
  if (res.status === 403) {
    const problem = (await res.json().catch(() => null)) as { title?: string } | null
    return { error: problem?.title ?? 'You need an invitation to join a household.' }
  }
  return { error: 'Sign-in failed. Please try again.' }
}

// On app start: if we hold a refresh token, get a fresh session (and the member). Null = signed out.
export async function restoreSession(): Promise<Me | null> {
  return getRefreshToken() ? doRefresh() : null
}

export async function signOut(): Promise<void> {
  const refreshToken = getRefreshToken()
  if (refreshToken) {
    await fetch(`${apiBaseUrl}/auth/mobile-logout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    }).catch(() => {})
  }
  clearTokens()
}

export type Member = {
  id: string
  displayName: string
  role: string
  email: string | null
  canSignIn: boolean
}

export type Invitation = {
  id: string
  code: string
  role: string
  email: string | null
  status: string
  expiresAt: string
}

// ----- Categories and payment methods (step 7) -----

export type Category = {
  id: string
  name: string
  archived: boolean
  isTaxable: boolean
}

export const paymentMethodTypes = ['CreditCard', 'BankAccount', 'Cash', 'Other'] as const
export type PaymentMethodType = (typeof paymentMethodTypes)[number]

export type PaymentMethod = {
  id: string
  label: string
  type: string
  archived: boolean
}

export type Vehicle = {
  id: string
  name: string
  make: string | null
  model: string | null
  year: number | null
  archived: boolean
}

// ----- Expenses (step 8) -----

export type LineItem = {
  id: string
  description: string
  categoryId: string
  category: string
  forMemberId: string | null
  for: string
  quantity: number
  unitPrice: number
  amount: number
  allocatedTax: number
  valueTag: string | null
  notes: string | null
  itemId: string | null
  shortForm: string | null
  vehicleId: string | null
  vehicle: string | null
}

export type Expense = {
  id: string
  merchant: string
  paymentMethodId: string
  paymentMethod: string
  date: string
  total: number
  tax: number | null
  notes: string | null
  source: string
  enteredBy: string
  lineItems: LineItem[]
}

// What the entry form sends for one line. The API fills in the item link and short form.
// For a receipt line, `description` is the printed text and `fullName` names the item (new or reused).
export type NewLineItem = {
  description: string
  categoryId: string
  forMemberId: string | null
  quantity: number | null
  unitPrice: number | null
  amount: number | null
  valueTag: string | null
  notes: string | null
  shortForm: string | null
  itemId?: string | null
  fullName?: string | null
  vehicleId?: string | null
}

export type NewExpense = {
  merchant: string
  paymentMethodId: string
  date: string | null
  tax: number | null
  notes: string | null
  lineItems: NewLineItem[]
  source?: string
  receiptBlobName?: string
}

// ----- Receipt capture (step 11) -----

export type ScanLine = {
  printedDescription: string
  quantity: number
  unitPrice: number
  amount: number
  matched: boolean
  itemId: string | null
  itemFullName: string | null
  suggestedCategoryId: string | null
  suggestedValueTag: string | null
}

export type ScanResult = {
  receiptBlobName: string
  merchant: string | null
  date: string | null
  total: number | null
  tax: number | null
  lineSum: number
  difference: number
  lines: ScanLine[]
}

// Multipart POST with the bearer token (used for file uploads, which don't go through apiFetch).
async function multipartPost(path: string, body: FormData): Promise<Response> {
  const token = await ensureFreshAccess()
  return fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body,
  })
}

// Uploads a receipt image (multipart) and returns the extracted, matched draft for review.
export async function scanReceipt(file: File): Promise<ScanResult> {
  const body = new FormData()
  body.append('file', file)
  const res = await multipartPost('/receipts/scan', body)
  if (res.ok) return (await res.json()) as ScanResult
  const problem = (await res.json().catch(() => null)) as { title?: string; errors?: Record<string, string[]> } | null
  const firstError = problem?.errors ? Object.values(problem.errors)[0]?.[0] : undefined
  throw new Error(firstError ?? problem?.title ?? 'Could not read that receipt.')
}

// Small typed wrappers over apiFetch, so components don't repeat URLs and JSON handling.
async function getJson<T>(path: string): Promise<T> {
  const res = await apiFetch(path)
  if (!res.ok) throw new Error(`GET ${path} failed: ${res.status}`)
  return (await res.json()) as T
}

export const getCategories = () => getJson<Category[]>('/categories')
export const getPaymentMethods = () => getJson<PaymentMethod[]>('/payment-methods')
export const getExpenses = () => getJson<Expense[]>('/expenses')

export const createCategory = (name: string, isTaxable = true) =>
  apiFetch('/categories', { method: 'POST', body: JSON.stringify({ name, isTaxable }) })

export const setCategoryTaxable = (id: string, isTaxable: boolean) =>
  apiFetch(`/categories/${id}`, { method: 'PATCH', body: JSON.stringify({ isTaxable }) })

export const createPaymentMethod = (label: string, type: PaymentMethodType) =>
  apiFetch('/payment-methods', { method: 'POST', body: JSON.stringify({ label, type }) })

export const archiveCategory = (id: string, archived: boolean) =>
  apiFetch(`/categories/${id}`, { method: 'PATCH', body: JSON.stringify({ archived }) })

export const archivePaymentMethod = (id: string, archived: boolean) =>
  apiFetch(`/payment-methods/${id}`, { method: 'PATCH', body: JSON.stringify({ archived }) })

export const getVehicles = () => getJson<Vehicle[]>('/vehicles')

export const createVehicle = (fields: { name: string; make?: string | null; model?: string | null; year?: number | null }) =>
  apiFetch('/vehicles', { method: 'POST', body: JSON.stringify(fields) })

export const archiveVehicle = (id: string, archived: boolean) =>
  apiFetch(`/vehicles/${id}`, { method: 'PATCH', body: JSON.stringify({ archived }) })

// ----- Item database (step 10) -----

export type ReceiptDescription = {
  merchant: string
  printedDescription: string
}

export type Item = {
  id: string
  fullName: string
  defaultCategoryId: string | null
  defaultCategory: string | null
  defaultValueTagId: string | null
  defaultValueTag: string | null
  hasPicture: boolean
  receiptDescriptions: ReceiptDescription[]
}

export function getItems(search?: string): Promise<Item[]> {
  const query = search && search.trim() !== '' ? `?search=${encodeURIComponent(search.trim())}` : ''
  return getJson<Item[]>(`/items${query}`)
}

// Replaces the item's editable fields. A null category / blank tag clears that default.
export function updateItem(
  id: string,
  fields: { fullName: string; defaultCategoryId: string | null; defaultValueTag: string | null },
): Promise<Response> {
  return apiFetch(`/items/${id}`, { method: 'PATCH', body: JSON.stringify(fields) })
}

export function mergeItems(targetId: string, sourceItemId: string): Promise<Response> {
  return apiFetch(`/items/${targetId}/merge`, { method: 'POST', body: JSON.stringify({ sourceItemId }) })
}

// Picture upload is multipart/form-data, so it does not use apiFetch's JSON Content-Type.
export function uploadItemPicture(id: string, file: File): Promise<Response> {
  const body = new FormData()
  body.append('file', file)
  return multipartPost(`/items/${id}/picture`, body)
}

// Fetches the picture with credentials and returns an object URL (more reliable than <img src> when
// third-party cookies are restricted). The caller must revoke the URL when done.
export async function getItemPictureUrl(id: string): Promise<string | null> {
  const res = await apiFetch(`/items/${id}/picture`)
  if (!res.ok) return null
  return URL.createObjectURL(await res.blob())
}

// The most recent purchase of an item, for the "last price / % change" banner during entry.
export type LastPrice = {
  fullName: string
  unitPrice: number
  amount: number
  quantity: number
  date: string
  merchant: string
}

export async function getItemLastPrice(name: string): Promise<LastPrice | null> {
  const res = await apiFetch(`/items/last-price?name=${encodeURIComponent(name)}`)
  if (res.status === 204 || !res.ok) return null
  return (await res.json()) as LastPrice
}

// ----- Reports (step 9) -----

export type Bucket = {
  key: string
  total: number
  count: number
}

export type ReportSummary = {
  from: string
  to: string
  total: number
  lineItemCount: number
  byCategory: Bucket[]
  byFor: Bucket[]
  byPaymentMethod: Bucket[]
  byMerchant: Bucket[]
  byItem: Bucket[]
  byValueTag: Bucket[]
  byVehicle: Bucket[]
}

// Both dates are optional; the API defaults to the current calendar month.
export function getReportSummary(from?: string, to?: string): Promise<ReportSummary> {
  const params = new URLSearchParams()
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  const query = params.toString()
  return getJson<ReportSummary>(`/reports/summary${query ? `?${query}` : ''}`)
}

export type TrendInterval = 'day' | 'week' | 'month'
export type TrendPoint = { period: string; total: number; count: number }
export type TrendResponse = { from: string; to: string; interval: string; points: TrendPoint[] }

export function getTrend(from?: string, to?: string, interval: TrendInterval = 'month'): Promise<TrendResponse> {
  const params = new URLSearchParams({ interval })
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  return getJson<TrendResponse>(`/reports/trend?${params.toString()}`)
}

export type PricePoint = { date: string; unitPrice: number; amount: number; quantity: number; merchant: string }
export type ItemPriceHistory = { itemId: string; fullName: string; points: PricePoint[] }

export function getItemPriceHistory(itemId: string, from?: string, to?: string): Promise<ItemPriceHistory> {
  const params = new URLSearchParams({ itemId })
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  return getJson<ItemPriceHistory>(`/reports/item-price-history?${params.toString()}`)
}

// ----- Fuel price hint (EIA) -----

export type FuelPrice = {
  area: string
  period: string
  dollarsPerGallon: number
}

// Latest weekly average regular-gas price for an EIA area (default Washington). Null when unavailable
// (no key configured, or the service returned nothing).
export async function getFuelPrice(area?: string): Promise<FuelPrice | null> {
  const res = await apiFetch(`/fuel-price${area ? `?area=${encodeURIComponent(area)}` : ''}`)
  if (res.status === 204 || !res.ok) return null
  return (await res.json()) as FuelPrice
}

// ----- Sales-tax rate lookup (Washington DOR) -----

export type TaxRate = {
  location: string
  combinedRate: number
  stateRate: number
  localRate: number
}

// Looks up the WA combined rate for a ZIP (optionally a street/city for a better match). Returns null
// when no Washington rate is found for the location.
export async function getTaxRate(zip: string, addr?: string, city?: string): Promise<TaxRate | null> {
  const params = new URLSearchParams({ zip })
  if (addr) params.set('addr', addr)
  if (city) params.set('city', city)
  const res = await apiFetch(`/tax-rate?${params.toString()}`)
  if (res.status === 404) return null
  if (!res.ok) throw new Error('Could not look up the tax rate.')
  return (await res.json()) as TaxRate
}

// Returns the created expense, or throws with the API's validation message.
export async function createExpense(expense: NewExpense): Promise<Expense> {
  const res = await apiFetch('/expenses', { method: 'POST', body: JSON.stringify(expense) })
  if (res.ok) return (await res.json()) as Expense
  const problem = (await res.json().catch(() => null)) as { title?: string; errors?: Record<string, string[]> } | null
  const firstError = problem?.errors ? Object.values(problem.errors)[0]?.[0] : undefined
  throw new Error(firstError ?? problem?.title ?? 'Could not save the expense.')
}
