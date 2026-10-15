import { useRef, useState } from 'react'
import {
  createExpense,
  importInvoice,
  replaceExpense,
  type Category,
  type Expense,
  type ImportResult,
  type Member,
  type NewExpense,
  type NewLineItem,
  type PaymentMethod,
  type Vehicle,
} from '../api'
import CategorySelect from './CategorySelect'

// One invoice draft waiting to be reviewed (the parsed result plus the file it came from).
type Draft = { fileName: string; result: ImportResult }

// A line being reviewed. Money/quantity are strings while editing. `include` lets the user drop a line
// that was flagged as already recorded (same item + same date).
type ReviewLine = {
  printedDescription: string
  itemName: string
  categoryId: string
  forMemberId: string
  vehicleId: string
  quantity: string
  unitPrice: string
  amount: string
  include: boolean
  possiblyDuplicate: boolean
}

function parseMoney(value: string): number | null {
  const n = Number.parseFloat(value)
  return Number.isFinite(n) ? n : null
}

// Per-file status while a batch of invoices is being read, shown as a list with progress bars.
type FileState = { name: string; status: 'queued' | 'reading' | 'done' | 'error'; note: string }

function summarize(r: ImportResult): string {
  if (r.duplicate) return 'Duplicate — already imported'
  if (r.lines.length === 0 && r.grandTotal == null) return 'No data read — enter manually'
  const amount = r.grandTotal != null ? ` · ${r.grandTotal.toFixed(2)}` : ''
  const matched = r.possibleMatch ? ' · matches an existing entry' : ''
  return `${r.vendor}${amount}${matched}`
}

export default function InvoiceImport({
  categories,
  paymentMethods,
  members,
  vehicles,
  onSaved,
  onAddCategory,
}: {
  categories: Category[]
  paymentMethods: PaymentMethod[]
  members: Member[]
  vehicles: Vehicle[]
  onSaved: (expense: Expense) => void
  onAddCategory: (name: string) => Promise<Category>
}) {
  const activeCategories = categories.filter((c) => !c.archived)
  const activeMethods = paymentMethods.filter((p) => !p.archived)
  const activeVehicles = vehicles.filter((v) => !v.archived)
  const firstCategoryId = activeCategories[0]?.id ?? ''
  // The Vehicle picker only applies to fuel lines, so it's shown only when the line's category is gas.
  const fuelCategoryIds = new Set(activeCategories.filter((c) => /gas|fuel/i.test(c.name)).map((c) => c.id))
  const isFuel = (categoryId: string) => fuelCategoryIds.has(categoryId)

  const fileInputRef = useRef<HTMLInputElement>(null)
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [index, setIndex] = useState(0)
  const [paymentMethodId, setPaymentMethodId] = useState(
    activeMethods.find((p) => p.isDefault)?.id ?? activeMethods[0]?.id ?? '',
  )
  const [merchant, setMerchant] = useState('')
  const [lines, setLines] = useState<ReviewLine[]>([])
  const [working, setWorking] = useState(false)
  const [fileStates, setFileStates] = useState<FileState[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const current = drafts[index] ?? null

  const toReviewLines = (result: ImportResult): ReviewLine[] =>
    result.lines.map((l) => ({
      printedDescription: l.printedDescription,
      itemName: l.itemFullName ?? l.printedDescription,
      categoryId: activeCategories.some((c) => c.id === l.suggestedCategoryId) ? l.suggestedCategoryId! : firstCategoryId,
      forMemberId: '',
      vehicleId: activeVehicles.some((v) => v.id === l.suggestedVehicleId) ? l.suggestedVehicleId! : '',
      quantity: String(l.quantity),
      unitPrice: String(l.unitPrice),
      amount: String(l.amount),
      // Flagged duplicates default to excluded so the same purchase is not recorded twice.
      include: !l.possiblyDuplicate,
      possiblyDuplicate: l.possiblyDuplicate,
    }))

  const showDraft = (list: Draft[], i: number) => {
    setIndex(i)
    const draft = i < list.length ? list[i] : null
    setLines(draft ? toReviewLines(draft.result) : [])
    // "Unknown" means nothing was read for the vendor; leave the field blank for the user to fill in.
    setMerchant(draft && draft.result.vendor !== 'Unknown' ? draft.result.vendor : '')
    // Default the payment method to the card detected on the bill, when it matches one we have.
    const suggested = draft?.result.suggestedPaymentMethodId
    if (suggested && activeMethods.some((p) => p.id === suggested)) {
      setPaymentMethodId(suggested)
    }
    setError(null)
  }

  const onPickFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    setError(null)
    setWorking(true)
    const chosen = Array.from(files)
    setFileStates(chosen.map((f) => ({ name: f.name, status: 'queued', note: 'Queued' })))
    const patch = (i: number, s: FileState['status'], note: string) =>
      setFileStates((prev) => prev.map((f, idx) => (idx === i ? { ...f, status: s, note } : f)))

    const collected: Draft[] = []
    try {
      for (let i = 0; i < chosen.length; i++) {
        patch(i, 'reading', 'Reading…')
        try {
          const result = await importInvoice(chosen[i])
          collected.push({ fileName: chosen[i].name, result })
          patch(i, 'done', summarize(result))
        } catch (err) {
          collected.push({
            fileName: chosen[i].name,
            // A failed read becomes an empty draft the user can fill in or skip.
            result: { invoiceBlobName: '', contentHash: '', vendor: 'Unknown', orderNumber: null, date: null, subtotal: null, tax: null, grandTotal: null, lineSum: 0, difference: 0, duplicate: null, possibleMatch: null, suggestedPaymentMethodId: null, lines: [] },
          })
          patch(i, 'error', err instanceof Error ? err.message : 'Could not read this file')
        }
      }
      setDrafts(collected)
      showDraft(collected, 0)
    } finally {
      setWorking(false)
    }
  }

  const updateLine = (i: number, patch: Partial<ReviewLine>) =>
    setLines((prev) => prev.map((l, idx) => (idx === i ? { ...l, ...patch } : l)))
  const removeLine = (i: number) => setLines((prev) => prev.filter((_, idx) => idx !== i))
  const addLine = () =>
    setLines((prev) => [
      ...prev,
      { printedDescription: '', itemName: '', categoryId: firstCategoryId, forMemberId: '', vehicleId: '', quantity: '1', unitPrice: '', amount: '', include: true, possiblyDuplicate: false },
    ])

  const includedSum = lines
    .filter((l) => l.include)
    .reduce((sum, l) => sum + (parseMoney(l.amount) ?? 0), 0)

  const nextDraft = () => {
    const i = index + 1
    if (i >= drafts.length) {
      setDrafts([])
      setIndex(0)
      setLines([])
    } else {
      showDraft(drafts, i)
    }
  }

  const buildPayload = (): NewExpense | null => {
    if (!current) return null
    if (merchant.trim() === '') {
      setError('Enter the merchant.')
      return null
    }
    if (paymentMethodId === '') {
      setError('Choose a payment method.')
      return null
    }
    const lineItems: NewLineItem[] = lines
      .filter((l) => l.include && (l.printedDescription.trim() !== '' || l.itemName.trim() !== ''))
      .map((l) => ({
        description: l.printedDescription.trim() || l.itemName.trim(),
        categoryId: l.categoryId,
        forMemberId: l.forMemberId === '' ? null : l.forMemberId,
        quantity: parseMoney(l.quantity),
        unitPrice: parseMoney(l.unitPrice),
        amount: parseMoney(l.amount),
        valueTag: null,
        notes: null,
        shortForm: null,
        fullName: l.itemName.trim() === '' ? null : l.itemName.trim(),
        vehicleId: l.vehicleId === '' ? null : l.vehicleId,
      }))
    if (lineItems.length === 0) {
      setError('Add at least one line (or skip this invoice).')
      return null
    }
    return {
      merchant: merchant.trim(),
      paymentMethodId,
      date: current.result.date,
      tax: current.result.tax,
      notes: null,
      lineItems,
      source: 'Invoice',
      invoiceBlobName: current.result.invoiceBlobName,
      orderNumber: current.result.orderNumber,
      invoiceContentHash: current.result.contentHash,
    }
  }

  // Create a new expense, or update the matched existing one (so it isn't duplicated).
  const persist = async (action: (payload: NewExpense) => Promise<Expense>) => {
    setError(null)
    const payload = buildPayload()
    if (!payload) return
    setSaving(true)
    try {
      const saved = await action(payload)
      onSaved(saved)
      nextDraft()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the expense.')
    } finally {
      setSaving(false)
    }
  }

  const save = () => persist(createExpense)
  const updateExisting = () => {
    const id = current?.result.possibleMatch?.id
    if (id) persist((payload) => replaceExpense(id, payload))
  }

  if (activeMethods.length === 0 || activeCategories.length === 0) {
    return (
      <p>
        Before importing invoices, add at least one payment method and one category under <strong>Settings</strong>.
      </p>
    )
  }

  // Step 1: choose invoice PDFs (one or many — e.g. everything in your Amazon-invoices folder).
  if (!current) {
    return (
      <div>
        <input
          ref={fileInputRef}
          type="file"
          accept="application/pdf,image/*"
          multiple
          hidden
          onChange={(e) => onPickFiles(e.target.files)}
        />
        {!working && (
          <div className="row receipt-capture">
            <button type="button" onClick={() => fileInputRef.current?.click()}>
              📄 Choose invoices or receipt photos
            </button>
          </div>
        )}

        {fileStates.length > 0 && (
          <ul className="import-files">
            {fileStates.map((f, i) => (
              <li key={i} className="import-file">
                <div className="import-file-name">
                  <span className="import-file-label">{f.name}</span>
                  <span className="import-file-note">{f.note}</span>
                </div>
                <div className={`progress-track ${f.status}`}>
                  <div
                    className={`progress-fill ${f.status}`}
                    style={{ width: f.status === 'done' || f.status === 'error' ? '100%' : f.status === 'queued' ? '0%' : undefined }}
                  />
                </div>
              </li>
            ))}
          </ul>
        )}

        {working && <p className="hint">Reading {fileStates.filter((f) => f.status === 'done' || f.status === 'error').length} of {fileStates.length}…</p>}
        {error && <p role="alert">{error}</p>}
        {!working && fileStates.length === 0 && (
          <p className="hint">
            Pick one or more bills — Amazon/Costco invoice PDFs or photos/scans of receipts (JPEG, PNG…).
            The app reads each one, itemizes it into separate lines, checks it is not already imported, and
            shows a draft to review. Nothing is saved until you approve it.
          </p>
        )}
      </div>
    )
  }

  // Step 2: review the current draft.
  const dup = current.result.duplicate
  const match = current.result.possibleMatch
  return (
    <div className="expense-form">
      <div className="row">
        <span className="grow">
          <strong>{current.result.vendor}</strong>
          {current.result.orderNumber && <> · order {current.result.orderNumber}</>}
          {current.result.date && <> · {current.result.date}</>}
          {current.result.grandTotal != null && <> · {current.result.grandTotal.toFixed(2)}</>}
        </span>
        <span className="hint">
          {current.fileName} ({index + 1} of {drafts.length})
        </span>
      </div>

      {dup && (
        <p role="alert" className="dup-banner">
          ⚠️ {dup.reason} It was imported on {new Date(dup.importedAt).toLocaleDateString()} as {dup.vendor}{' '}
          {dup.grandTotal.toFixed(2)}. You can skip it, or import anyway if this is a different order.
        </p>
      )}

      {!dup && match && (
        <p className="dup-banner">
          ℹ️ This looks like an existing entry: <strong>{match.merchant} {match.total.toFixed(2)}</strong> on{' '}
          {match.date} ({match.source.toLowerCase()}, {match.lineCount} line{match.lineCount === 1 ? '' : 's'}).
          Use <strong>Update existing entry</strong> to replace it instead of adding a duplicate.
        </p>
      )}

      <div className="row">
        <label className="grow">
          Merchant
          <input value={merchant} onChange={(e) => setMerchant(e.target.value)} placeholder="e.g. Costco" />
        </label>
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
      </div>
      <p className="hint">This payment method applies to the whole bill — every item below is paid with it.</p>

      {current.result.grandTotal != null && Math.abs(includedSum - current.result.grandTotal) >= 0.01 && (
        <p className="hint">
          Order total is {current.result.grandTotal.toFixed(2)}; included lines add up to {includedSum.toFixed(2)}.
          {current.result.tax != null && <> Tax {current.result.tax.toFixed(2)} is spread across taxable lines on save.</>}
        </p>
      )}

      <h3>
        Items
        {activeMethods.find((p) => p.id === paymentMethodId)?.label && (
          <span className="hint"> · all paid with {activeMethods.find((p) => p.id === paymentMethodId)!.label}</span>
        )}
      </h3>
      {lines.map((line, i) => (
        <fieldset key={i} className={`line${line.possiblyDuplicate ? ' line-duplicate' : ''}`}>
          <div className="row">
            <label className="checkbox">
              <input type="checkbox" checked={line.include} onChange={(e) => updateLine(i, { include: e.target.checked })} />
              Include
            </label>
            {line.possiblyDuplicate && <span className="hint"> · possibly already recorded on this date</span>}
            <button type="button" onClick={() => removeLine(i)} style={{ marginLeft: 'auto' }}>
              Remove
            </button>
          </div>
          <div className="row">
            <label className="grow">
              Item name
              <input value={line.itemName} onChange={(e) => updateLine(i, { itemName: e.target.value })} placeholder="Item name" />
            </label>
            <label>
              Category
              <CategorySelect
                value={line.categoryId}
                categories={activeCategories}
                onAddCategory={onAddCategory}
                onChange={(id) => updateLine(i, isFuel(id) ? { categoryId: id } : { categoryId: id, vehicleId: '' })}
              />
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
            {activeVehicles.length > 0 && isFuel(line.categoryId) && (
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
          </div>
        </fieldset>
      ))}

      <div className="row">
        <button type="button" onClick={addLine}>
          + Add line
        </button>
        <span className="total">Included: {includedSum.toFixed(2)}</span>
      </div>

      {error && <p role="alert">{error}</p>}
      <div className="row">
        {match && !dup && (
          <button type="button" onClick={updateExisting} disabled={saving}>
            {saving ? 'Updating…' : 'Update existing entry'}
          </button>
        )}
        <button type="button" onClick={save} disabled={saving}>
          {saving ? 'Saving…' : match && !dup ? 'Save as new' : 'Save expense'}
        </button>
        <button type="button" onClick={nextDraft} disabled={saving}>
          Skip this invoice
        </button>
      </div>
    </div>
  )
}
