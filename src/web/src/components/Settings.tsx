import { useCallback, useEffect, useState } from 'react'
import {
  archiveCategory,
  archivePaymentMethod,
  createCategory,
  createPaymentMethod,
  getCategories,
  getPaymentMethods,
  paymentMethodTypes,
  type Category,
  type Me,
  type PaymentMethod,
  type PaymentMethodType,
} from '../api'

export default function Settings({ me }: { me: Me }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([])
  const isParent = me.role === 'Parent'

  const load = useCallback(() => {
    getCategories().then(setCategories).catch(() => {})
    getPaymentMethods().then(setPaymentMethods).catch(() => {})
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
              {c.archived && ' (archived)'}
              {isParent && (
                <>
                  {' '}
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
              {p.archived && ' (archived)'}
              {isParent && (
                <>
                  {' '}
                  <button onClick={() => archivePaymentMethod(p.id, !p.archived).then(load)}>
                    {p.archived ? 'Restore' : 'Archive'}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
        {isParent && <AddPaymentMethod onAdded={load} />}
      </section>
    </>
  )
}

function AddCategory({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (name.trim() === '') return
    const res = await createCategory(name.trim())
    if (res.ok) {
      setName('')
      setError(null)
      onAdded()
    } else {
      setError('That category already exists.')
    }
  }

  return (
    <div>
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="New category" />
      <button onClick={submit}>Add</button>
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
