import { useCallback, useEffect, useState } from 'react'
import {
  archiveCategory,
  archivePaymentMethod,
  archiveVehicle,
  createCategory,
  createPaymentMethod,
  createVehicle,
  setCategoryTaxable,
  setDefaultPaymentMethod,
  getCategories,
  getPaymentMethods,
  getVehicles,
  paymentMethodTypes,
  type Category,
  type Me,
  type PaymentMethod,
  type PaymentMethodType,
  type Vehicle,
} from '../api'
import { guessTaxable } from '../taxable'

export default function Settings({ me }: { me: Me }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([])
  const [vehicles, setVehicles] = useState<Vehicle[]>([])
  const isParent = me.role === 'Parent'

  const load = useCallback(() => {
    getCategories().then(setCategories).catch(() => {})
    getPaymentMethods().then(setPaymentMethods).catch(() => {})
    getVehicles().then(setVehicles).catch(() => {})
  }, [])

  useEffect(load, [load])

  return (
    <>
      <section>
        <h2>Categories</h2>
        <ul>
          {categories.map((c) => (
            <li key={c.id}>
              {c.name}
              {!c.isTaxable && ' · non-taxable'}
              {c.archived && ' (archived)'}
              {isParent && (
                <>
                  {' '}
                  <button onClick={() => setCategoryTaxable(c.id, !c.isTaxable).then(load)}>
                    {c.isTaxable ? 'Mark non-taxable' : 'Mark taxable'}
                  </button>{' '}
                  <button onClick={() => archiveCategory(c.id, !c.archived).then(load)}>
                    {c.archived ? 'Restore' : 'Archive'}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
        {isParent && <AddCategory onAdded={load} />}
      </section>

      <section>
        <h2>Payment methods</h2>
        <ul>
          {paymentMethods.map((p) => (
            <li key={p.id}>
              {p.label} — {p.type}
              {p.isDefault && ' · ⭐ default'}
              {p.archived && ' (archived)'}
              {isParent && (
                <>
                  {' '}
                  {!p.isDefault && !p.archived && (
                    <button onClick={() => setDefaultPaymentMethod(p.id).then(load)}>Make default</button>
                  )}{' '}
                  <button onClick={() => archivePaymentMethod(p.id, !p.archived).then(load)}>
                    {p.archived ? 'Restore' : 'Archive'}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
        {isParent && (
          <p className="hint">The ⭐ default payment method is pre-selected on new and imported expenses.</p>
        )}
        {isParent && <AddPaymentMethod onAdded={load} />}
      </section>

      <section>
        <h2>Vehicles</h2>
        <p className="hint">Add your cars so gas and other per-car costs can be tracked and reported per vehicle.</p>
        <ul>
          {vehicles.map((v) => (
            <li key={v.id}>
              {v.name}
              {(v.make || v.model || v.year) && (
                <> — {[v.year, v.make, v.model].filter(Boolean).join(' ')}</>
              )}
              {v.archived && ' (archived)'}
              {isParent && (
                <>
                  {' '}
                  <button onClick={() => archiveVehicle(v.id, !v.archived).then(load)}>
                    {v.archived ? 'Restore' : 'Archive'}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
        {isParent && <AddVehicle onAdded={load} />}
      </section>
    </>
  )
}

function AddVehicle({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('')
  const [make, setMake] = useState('')
  const [model, setModel] = useState('')
  const [year, setYear] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (name.trim() === '') return
    const parsedYear = year.trim() === '' ? null : Number.parseInt(year, 10)
    const res = await createVehicle({
      name: name.trim(),
      make: make.trim() || null,
      model: model.trim() || null,
      year: Number.isFinite(parsedYear) ? parsedYear : null,
    })
    if (res.ok) {
      setName('')
      setMake('')
      setModel('')
      setYear('')
      setError(null)
      onAdded()
    } else {
      setError('That vehicle already exists.')
    }
  }

  return (
    <div className="row">
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Name e.g. Honda Pilot" />
      <input value={make} onChange={(e) => setMake(e.target.value)} placeholder="Make (optional)" />
      <input value={model} onChange={(e) => setModel(e.target.value)} placeholder="Model (optional)" />
      <input
        type="number"
        value={year}
        onChange={(e) => setYear(e.target.value)}
        placeholder="Year"
        style={{ width: '5rem' }}
      />
      <button onClick={submit}>Add</button>
      {error && <p role="alert">{error}</p>}
    </div>
  )
}

function AddCategory({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('')
  const [isTaxable, setIsTaxable] = useState(true)
  // Until the user ticks the box themselves, keep the suggestion in sync with the name (WA groceries
  // are non-taxable). Once they override it, stop guessing.
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const changeName = (value: string) => {
    setName(value)
    if (!touched) setIsTaxable(guessTaxable(value))
  }

  const submit = async () => {
    if (name.trim() === '') return
    const res = await createCategory(name.trim(), isTaxable)
    if (res.ok) {
      setName('')
      setIsTaxable(true)
      setTouched(false)
      setError(null)
      onAdded()
    } else {
      setError('That category already exists.')
    }
  }

  return (
    <div>
      <div className="row">
        <input value={name} onChange={(e) => changeName(e.target.value)} placeholder="New category" />
        <label className="checkbox">
          <input
            type="checkbox"
            checked={isTaxable}
            onChange={(e) => {
              setTouched(true)
              setIsTaxable(e.target.checked)
            }}
          />{' '}
          Taxable
        </label>
        <button onClick={submit}>Add</button>
      </div>
      <p className="hint">
        Uncheck <strong>Taxable</strong> for non-taxable items like groceries and produce (not taxed in WA).
        We guess from the name — adjust if needed.
      </p>
      {error && <p role="alert">{error}</p>}
    </div>
  )
}

function AddPaymentMethod({ onAdded }: { onAdded: () => void }) {
  const [label, setLabel] = useState('')
  const [type, setType] = useState<PaymentMethodType>('CreditCard')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (label.trim() === '') return
    const res = await createPaymentMethod(label.trim(), type)
    if (res.ok) {
      setLabel('')
      setError(null)
      onAdded()
    } else {
      setError('That payment method already exists.')
    }
  }

  return (
    <div>
      <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="e.g. Discover card" />
      <select value={type} onChange={(e) => setType(e.target.value as PaymentMethodType)}>
        {paymentMethodTypes.map((t) => (
          <option key={t} value={t}>
            {t}
          </option>
        ))}
      </select>
      <button onClick={submit}>Add</button>
      <p className="hint">Label only — never a card or account number (SPEC feature 1).</p>
      {error && <p role="alert">{error}</p>}
    </div>
  )
}
