import { useEffect, useMemo, useState } from 'react'
import {
  createExpense,
  getItems,
  scanReceipt,
  type Category,
  type Expense,
  type Item,
  type Member,
  type NewLineItem,
  type PaymentMethod,
  type ScanLine,
  type ScanResult,
} from '../api'

// A line being reviewed. Money/quantity are strings while editing. `itemName` is the item's full name
// (prefilled when recognized); typing a name reuses an existing item or creates a new one on save.
type ReviewLine = {
  printedDescription: string
  itemName: string
  matched: boolean
  categoryId: string
  forMemberId: string
  quantity: string
  unitPrice: string
  amount: string
  valueTag: string
  notes: string
}

function parseMoney(value: string): number | null {
  const n = Number.parseFloat(value)
  return Number.isFinite(n) ? n : null
}

export default function ReceiptScan({
  categories,
  paymentMethods,
  members,
  onSaved,
}: {
  categories: Category[]
  paymentMethods: PaymentMethod[]
  members: Member[]
  onSaved: (expense: Expense) => void
}) {
  const activeCategories = categories.filter((c) => !c.archived)
  const activeMethods = paymentMethods.filter((p) => !p.archived)
  const firstCategoryId = activeCategories[0]?.id ?? ''

  const [items, setItems] = useState<Item[]>([])
  const [draft, setDraft] = useState<ScanResult | null>(null)
  const [merchant, setMerchant] = useState('')
  const [date, setDate] = useState('')
  const [paymentMethodId, setPaymentMethodId] = useState(activeMethods[0]?.id ?? '')
  const [tax, setTax] = useState('')
  const [lines, setLines] = useState<ReviewLine[]>([])
  const [scanning, setScanning] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Item names and value tags for autocomplete, so the same item/tag is reused instead of re-created.
  useEffect(() => {
    getItems().then(setItems).catch(() => {})
  }, [])
  const nameSuggestions = useMemo(() => items.map((i) => i.fullName).sort(), [items])
  const tagSuggestions = useMemo(
    () => [...new Set(items.map((i) => i.defaultValueTag).filter((t): t is string => !!t))].sort(),
    [items],
  )

  const toReviewLine = (l: ScanLine): ReviewLine => {
    const suggested = l.suggestedCategoryId
    const categoryId = activeCategories.some((c) => c.id === suggested) ? suggested! : firstCategoryId
    return {
      printedDescription: l.printedDescription,
      itemName: l.itemFullName ?? '',
      matched: l.matched,
      categoryId,
      forMemberId: '',
      quantity: String(l.quantity),
      unitPrice: String(l.unitPrice),
      amount: String(l.amount),
      valueTag: l.suggestedValueTag ?? '',
      notes: '',
    }
  }

  const onPickFile = async (file: File | undefined) => {
    if (!file) return
    setError(null)
    setScanning(true)
    try {
      const result = await scanReceipt(file)
      setDraft(result)
      setMerchant(result.merchant ?? '')
      setDate(result.date ?? new Date().toISOString().slice(0, 10))
      setTax(result.tax != null ? String(result.tax) : '')
      setLines(result.lines.map(toReviewLine))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not read that receipt.')
    } finally {
      setScanning(false)
    }
  }

  const updateLine = (index: number, patch: Partial<ReviewLine>) =>
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  const removeLine = (index: number) => setLines((prev) => prev.filter((_, i) => i !== index))
  const addLine = () =>
    setLines((prev) => [
      ...prev,
      { printedDescription: '', itemName: '', matched: false, categoryId: firstCategoryId, forMemberId: '', quantity: '1', unitPrice: '', amount: '', valueTag: '', notes: '' },
    ])

  const lineSum = lines.reduce((sum, l) => {
    const amount = parseMoney(l.amount)
    if (amount !== null) return sum + amount
    const qty = parseMoney(l.quantity) ?? 1
    const unit = parseMoney(l.unitPrice) ?? 0
    return sum + qty * unit
  }, 0)
  const difference = draft?.total != null ? draft.total - lineSum : 0

  const reset = () => {
    setDraft(null)
    setLines([])
    setError(null)
  }

  const save = async () => {
    setError(null)
    if (merchant.trim() === '') {
      setError('Enter the merchant.')
      return
    }
    const lineItems: NewLineItem[] = lines
      .filter((l) => l.printedDescription.trim() !== '' || l.itemName.trim() !== '')
      .map((l) => ({
        description: l.printedDescription.trim() || l.itemName.trim(),
        categoryId: l.categoryId,
        forMemberId: l.forMemberId === '' ? null : l.forMemberId,
        quantity: parseMoney(l.quantity),
        unitPrice: parseMoney(l.unitPrice),
        amount: parseMoney(l.amount),
        valueTag: l.valueTag.trim() === '' ? null : l.valueTag.trim(),
        notes: l.notes.trim() === '' ? null : l.notes.trim(),
        shortForm: null,
        fullName: l.itemName.trim() === '' ? null : l.itemName.trim(),
      }))
    if (lineItems.length === 0) {
      setError('Add at least one line.')
      return
    }

    setSaving(true)
    try {
      const saved = await createExpense({
        merchant: merchant.trim(),
        paymentMethodId,
        date: date || null,
        tax: parseMoney(tax),
        notes: null,
        lineItems,
        source: 'Receipt',
        receiptBlobName: draft!.receiptBlobName,
      })
      onSaved(saved)
      reset()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the expense.')
    } finally {
      setSaving(false)
    }
  }

  if (activeMethods.length === 0 || activeCategories.length === 0) {
    return (
      <p>
        Before scanning a receipt, add at least one payment method and one category under{' '}
        <strong>Settings</strong>.
      </p>
    )
  }

  // Step 1: choose/take a photo.
  if (!draft) {
    return (
      <div>
        <label>
          Receipt photo
          <input type="file" accept="image/*" capture="environment" onChange={(e) => onPickFile(e.target.files?.[0])} />
        </label>
        {scanning && <p>Reading the receipt…</p>}
        {error && <p role="alert">{error}</p>}
        <p className="hint">
          On a phone this opens the camera. The app reads the printed text and matches each line to your item
          database — nothing is saved until you review it below.
        </p>
      </div>
    )
  }

  // Step 2: review the draft.
  return (
    <div className="expense-form">
      <div className="row">
        <label className="grow">
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
          Tax (optional)
          <input type="number" step="0.01" min="0" value={tax} onChange={(e) => setTax(e.target.value)} />
        </label>
      </div>

      {draft.total != null && Math.abs(difference) >= 0.01 && (
        <p role="alert" className="hint">
          Heads up: the receipt total is {draft.total.toFixed(2)} but the lines add up to {lineSum.toFixed(2)} (a
          difference of {difference.toFixed(2)}). Adjust the lines or tax if needed.
        </p>
      )}

      <h3>Items</h3>
      {lines.map((line, i) => (
        <fieldset key={i} className={`line${line.itemName.trim() === '' ? ' line-unmatched' : ''}`}>
          <div className="row">
            <div className="grow">
              <span className="short-form">{line.printedDescription || '(added line)'}</span>
              {line.matched ? (
                <span className="matched-badge"> ✓ recognized</span>
              ) : (
                <span className="hint"> · what is this item?</span>
              )}
            </div>
            {lines.length > 1 && (
              <button type="button" onClick={() => removeLine(i)}>
                Remove
              </button>
            )}
          </div>
          <div className="row">
            <label className="grow">
              Item name
              <input
                list="receipt-item-names"
                value={line.itemName}
                onChange={(e) => updateLine(i, { itemName: e.target.value })}
                placeholder="Full name, e.g. Kirkland Signature Organic Eggs"
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
          </div>
          <div className="row">
            <label>
              Qty
              <input type="number" step="0.001" min="0" value={line.quantity} onChange={(e) => updateLine(i, { quantity: e.target.value })} />
            </label>
            <label>
              Unit price
              <input type="number" step="0.01" min="0" value={line.unitPrice} onChange={(e) => updateLine(i, { unitPrice: e.target.value })} />
            </label>
            <label>
              Amount
              <input type="number" step="0.01" min="0" value={line.amount} onChange={(e) => updateLine(i, { amount: e.target.value })} />
            </label>
            <label>
              Value tag
              <input list="receipt-value-tags" value={line.valueTag} onChange={(e) => updateLine(i, { valueTag: e.target.value })} placeholder="e.g. Splurge" />
            </label>
          </div>
          <label>
            Note (optional)
            <input value={line.notes} onChange={(e) => updateLine(i, { notes: e.target.value })} />
          </label>
        </fieldset>
      ))}

      <datalist id="receipt-item-names">
        {nameSuggestions.map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>
      <datalist id="receipt-value-tags">
        {tagSuggestions.map((t) => (
          <option key={t} value={t} />
        ))}
      </datalist>

      <div className="row">
        <button type="button" onClick={addLine}>
          + Add line
        </button>
        <span className="total">Lines total: {lineSum.toFixed(2)}</span>
      </div>

      {error && <p role="alert">{error}</p>}
      <div className="row">
        <button type="button" onClick={save} disabled={saving}>
          {saving ? 'Saving…' : 'Save expense'}
        </button>
        <button type="button" onClick={reset}>
          Discard &amp; start over
        </button>
      </div>
      <p className="hint">
        Lines you leave unnamed are saved with just their printed text; name them to teach the item database.
      </p>
    </div>
  )
}
