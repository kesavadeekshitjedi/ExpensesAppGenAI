import { useState, type FormEvent } from 'react'
import {
  createExpense,
  getTaxRate,
  type Category,
  type Expense,
  type Member,
  type NewLineItem,
  type PaymentMethod,
  type Vehicle,
} from '../api'

// The form keeps money/quantity fields as strings while typing (empty is allowed), then parses them
// on submit. A line's `for` is a member id, or '' meaning Family (sent to the API as null).
type MoneyField = 'quantity' | 'unitPrice' | 'amount'
const MONEY_FIELDS: MoneyField[] = ['quantity', 'unitPrice', 'amount']

type LineDraft = {
  description: string
  categoryId: string
  forMemberId: string
  quantity: string
  unitPrice: string
  amount: string
  // The two money fields the user most recently specified (most-recent first). The remaining field is
  // the one we auto-compute, so entering any two of {qty, unit price, amount} fills in the third —
  // e.g. for gas, total + $/gal gives gallons.
  recent: [MoneyField, MoneyField]
  vehicleId: string // '' = not a per-vehicle cost
  valueTag: string
  notes: string
}

function emptyLine(categoryId: string, forMemberId: string): LineDraft {
  return {
    description: '',
    categoryId,
    forMemberId,
    quantity: '1',
    unitPrice: '',
    amount: '',
    recent: ['unitPrice', 'quantity'], // so amount is the field computed from qty × unit price by default
    vehicleId: '',
    valueTag: '',
    notes: '',
  }
}

// Re-solve a line after the user edits one money field. The edited field becomes the most-recent; the
// field that is neither of the two most-recent is recomputed from them when both have values.
function solveLine(line: LineDraft, edited: MoneyField): LineDraft {
  const second = line.recent[0] === edited ? line.recent[1] : line.recent[0]
  const recent: [MoneyField, MoneyField] = [edited, second]
  const derived = MONEY_FIELDS.find((f) => f !== recent[0] && f !== recent[1])!

  const next: LineDraft = { ...line, recent }
  const q = parseMoney(next.quantity)
  const u = parseMoney(next.unitPrice)
  const a = parseMoney(next.amount)

  if (derived === 'amount' && q !== null && u !== null) {
    next.amount = (q * u).toFixed(2)
  } else if (derived === 'unitPrice' && a !== null && q) {
    next.unitPrice = (a / q).toFixed(2)
  } else if (derived === 'quantity' && a !== null && u) {
    // Quantity (e.g. gallons) can be fractional; keep up to 3 decimals without trailing zeros.
    next.quantity = String(Math.round((a / u) * 1000) / 1000)
  }
  return next
}

function today(): string {
  return new Date().toISOString().slice(0, 10)
}

function parseMoney(value: string): number | null {
  const n = Number.parseFloat(value)
  return Number.isFinite(n) ? n : null
}

export default function ExpenseForm({
  categories,
  paymentMethods,
  members,
  vehicles,
  tagSuggestions,
  onSaved,
}: {
  categories: Category[]
  paymentMethods: PaymentMethod[]
  members: Member[]
  vehicles: Vehicle[]
  tagSuggestions: string[]
  onSaved: (expense: Expense) => void
}) {
  const activeCategories = categories.filter((c) => !c.archived)
  const activeMethods = paymentMethods.filter((p) => !p.archived)
  const activeVehicles = vehicles.filter((v) => !v.archived)
  const firstCategoryId = activeCategories[0]?.id ?? ''

  const [merchant, setMerchant] = useState('')
  const [paymentMethodId, setPaymentMethodId] = useState(activeMethods[0]?.id ?? '')
  const [date, setDate] = useState(today())
  const [tax, setTax] = useState('')
  const [notes, setNotes] = useState('')
  // The default "for" pre-fills each new line; it can be changed per line (SPEC feature 2).
  const [defaultFor, setDefaultFor] = useState('')
  const [lines, setLines] = useState<LineDraft[]>([emptyLine(firstCategoryId, '')])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  // WA tax-rate estimate helper.
  const [zip, setZip] = useState('')
  const [rateNote, setRateNote] = useState<string | null>(null)

  const canSubmit = activeMethods.length > 0 && activeCategories.length > 0

  const updateLine = (index: number, patch: Partial<LineDraft>) =>
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))

  // Edit one of the three money fields; the solver recomputes whichever of the other two is "free".
  const changeMoney = (index: number, field: MoneyField, value: string) =>
    setLines((prev) => prev.map((l, i) => (i === index ? solveLine({ ...l, [field]: value }, field) : l)))

  const addLine = () => setLines((prev) => [...prev, emptyLine(firstCategoryId, defaultFor)])
  const removeLine = (index: number) => setLines((prev) => prev.filter((_, i) => i !== index))

  // Look up the WA combined rate for the ZIP and fill the Tax field = rate × taxable subtotal.
  const estimateTax = async () => {
    setRateNote(null)
    if (zip.trim() === '') return
    try {
      const rate = await getTaxRate(zip.trim())
      if (!rate) {
        setRateNote('No Washington rate found for that ZIP.')
        return
      }
      const taxableIds = new Set(categories.filter((c) => c.isTaxable).map((c) => c.id))
      const taxableSubtotal = lines.reduce((sum, l) => {
        if (!taxableIds.has(l.categoryId)) return sum
        const amount = parseMoney(l.amount)
        if (amount !== null) return sum + amount
        const qty = parseMoney(l.quantity) ?? 1
        const unit = parseMoney(l.unitPrice) ?? 0
        return sum + qty * unit
      }, 0)
      setTax((taxableSubtotal * rate.combinedRate).toFixed(2))
      setRateNote(
        `${rate.location}: ${(rate.combinedRate * 100).toFixed(1)}% on taxable ${taxableSubtotal.toFixed(2)}`,
      )
    } catch {
      setRateNote('Tax lookup failed.')
    }
  }

  const estimatedTotal = lines.reduce((sum, l) => {
    const amount = parseMoney(l.amount)
    if (amount !== null) return sum + amount
    const qty = parseMoney(l.quantity) ?? 1
    const unit = parseMoney(l.unitPrice) ?? 0
    return sum + qty * unit
  }, 0)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)

    const lineItems: NewLineItem[] = lines
      .filter((l) => l.description.trim() !== '')
      .map((l) => ({
        description: l.description.trim(),
        categoryId: l.categoryId,
        forMemberId: l.forMemberId === '' ? null : l.forMemberId,
        quantity: parseMoney(l.quantity),
        unitPrice: parseMoney(l.unitPrice),
        amount: parseMoney(l.amount),
        valueTag: l.valueTag.trim() === '' ? null : l.valueTag.trim(),
        notes: l.notes.trim() === '' ? null : l.notes.trim(),
        shortForm: null, // let the API figure out the short form
        vehicleId: l.vehicleId === '' ? null : l.vehicleId,
      }))

    if (merchant.trim() === '') {
      setError('Enter the merchant (where you spent the money).')
      return
    }
    if (lineItems.length === 0) {
      setError('Add at least one line item with a description.')
      return
    }
    if (lineItems.some((l) => l.amount === null)) {
      setError('Enter an amount for each item (or a unit price so it can be calculated).')
      return
    }

    setSaving(true)
    try {
      const saved = await createExpense({
        merchant: merchant.trim(),
        paymentMethodId,
        date,
        tax: parseMoney(tax),
        notes: notes.trim() === '' ? null : notes.trim(),
        lineItems,
      })
      // Reset for the next entry, keeping merchant/payment method handy.
      setLines([emptyLine(firstCategoryId, defaultFor)])
      setNotes('')
      setTax('')
      onSaved(saved)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the expense.')
    } finally {
      setSaving(false)
    }
  }

  if (!canSubmit) {
    return (
      <p>
        Before entering an expense, add at least one{' '}
        {activeMethods.length === 0 ? 'payment method' : 'category'} under <strong>Settings</strong>.
      </p>
    )
  }

  return (
    <form onSubmit={submit} className="expense-form">
      <div className="row">
        <label>
          Merchant
          <input value={merchant} onChange={(e) => setMerchant(e.target.value)} placeholder="e.g. Costco" />
        </label>
        <label>
          Date
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </label>
      </div>

      <div className="row">
        <label>
          Payment method
          <select value={paymentMethodId} onChange={(e) => setPaymentMethodId(e.target.value)}>
            {activeMethods.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Default “for”
          <select value={defaultFor} onChange={(e) => setDefaultFor(e.target.value)}>
            <option value="">Family</option>
            {members.map((m) => (
              <option key={m.id} value={m.id}>
                {m.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Tax (optional)
          <input type="number" step="0.01" min="0" value={tax} onChange={(e) => setTax(e.target.value)} />
        </label>
        <label>
          ZIP (WA tax estimate)
          <input value={zip} onChange={(e) => setZip(e.target.value)} placeholder="e.g. 98052" inputMode="numeric" />
        </label>
        <button type="button" onClick={estimateTax}>
          Estimate tax
        </button>
      </div>
      {rateNote && <p className="hint">{rateNote}</p>}

      <h3>Items</h3>
      {lines.map((line, i) => (
        <fieldset key={i} className="line">
          <div className="row">
            <label className="grow">
              Description
              <input
                value={line.description}
                onChange={(e) => updateLine(i, { description: e.target.value })}
                placeholder="e.g. Kirkland Organic Eggs"
              />
            </label>
            <label>
              Category
              <select value={line.categoryId} onChange={(e) => updateLine(i, { categoryId: e.target.value })}>
                {activeCategories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              For
              <select value={line.forMemberId} onChange={(e) => updateLine(i, { forMemberId: e.target.value })}>
                <option value="">Family</option>
                {members.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.displayName}
                  </option>
                ))}
              </select>
            </label>
            {activeVehicles.length > 0 && (
              <label>
                Vehicle
                <select value={line.vehicleId} onChange={(e) => updateLine(i, { vehicleId: e.target.value })}>
                  <option value="">— none —</option>
                  {activeVehicles.map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
          <div className="row">
            <label>
              Qty
              <input type="number" step="1" min="0" value={line.quantity} onChange={(e) => changeMoney(i, 'quantity', e.target.value)} />
            </label>
            <label>
              Unit price
              <input type="number" step="0.01" min="0" value={line.unitPrice} onChange={(e) => changeMoney(i, 'unitPrice', e.target.value)} />
            </label>
            <label>
              Amount
              <input
                type="number"
                step="0.01"
                min="0"
                required
                value={line.amount}
                onChange={(e) => changeMoney(i, 'amount', e.target.value)}
                placeholder="auto from any two"
              />
            </label>
            <label>
              Value tag
              <input list="value-tags" value={line.valueTag} onChange={(e) => updateLine(i, { valueTag: e.target.value })} placeholder="e.g. Splurge" />
            </label>
          </div>
          <label>
            Note (optional)
            <input value={line.notes} onChange={(e) => updateLine(i, { notes: e.target.value })} placeholder="e.g. needed for school project" />
          </label>
          {lines.length > 1 && (
            <button type="button" onClick={() => removeLine(i)}>
              Remove item
            </button>
          )}
        </fieldset>
      ))}

      <datalist id="value-tags">
        {tagSuggestions.map((t) => (
          <option key={t} value={t} />
        ))}
      </datalist>

      <div className="row">
        <button type="button" onClick={addLine}>
          + Add item
        </button>
        <span className="total">Total: {estimatedTotal.toFixed(2)}</span>
      </div>

      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={saving}>
        {saving ? 'Saving…' : 'Save expense'}
      </button>
      <p className="hint">
        Tip: type the full item name and the app figures out its receipt short form for you. Enter any
        two of Qty / Unit price / Amount and the third fills in — e.g. for gas, total + price-per-gallon
        gives the gallons.
      </p>
    </form>
  )
}
