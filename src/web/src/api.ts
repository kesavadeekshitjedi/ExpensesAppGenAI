const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

// All API calls include credentials so the session cookie set by /auth/session is sent back.
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  return fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  })
}

export type Me = {
  memberId: string
  householdId: string
  role: 'Parent' | 'Child'
  displayName: string
  email: string | null
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
}

export const paymentMethodTypes = ['CreditCard', 'BankAccount', 'Cash', 'Other'] as const
export type PaymentMethodType = (typeof paymentMethodTypes)[number]

export type PaymentMethod = {
  id: string
  label: string
  type: string
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
  valueTag: string | null
  notes: string | null
  itemId: string | null
  shortForm: string | null
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
}

export type NewExpense = {
  merchant: string
  paymentMethodId: string
  date: string | null
  tax: number | null
  notes: string | null
  lineItems: NewLineItem[]
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

export const createCategory = (name: string) =>
  apiFetch('/categories', { method: 'POST', body: JSON.stringify({ name }) })

export const createPaymentMethod = (label: string, type: PaymentMethodType) =>
  apiFetch('/payment-methods', { method: 'POST', body: JSON.stringify({ label, type }) })

export const archiveCategory = (id: string, archived: boolean) =>
  apiFetch(`/categories/${id}`, { method: 'PATCH', body: JSON.stringify({ archived }) })

export const archivePaymentMethod = (id: string, archived: boolean) =>
  apiFetch(`/payment-methods/${id}`, { method: 'PATCH', body: JSON.stringify({ archived }) })

// Returns the created expense, or throws with the API's validation message.
export async function createExpense(expense: NewExpense): Promise<Expense> {
  const res = await apiFetch('/expenses', { method: 'POST', body: JSON.stringify(expense) })
  if (res.ok) return (await res.json()) as Expense
  const problem = (await res.json().catch(() => null)) as { title?: string; errors?: Record<string, string[]> } | null
  const firstError = problem?.errors ? Object.values(problem.errors)[0]?.[0] : undefined
  throw new Error(firstError ?? problem?.title ?? 'Could not save the expense.')
}
