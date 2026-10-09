import { useCallback, useEffect, useState } from 'react'
import {
  getCategories,
  getExpenses,
  getPaymentMethods,
  type Category,
  type Expense,
  type Me,
  type Member,
  type PaymentMethod,
} from '../api'
import ExpenseForm from './ExpenseForm'
import ReceiptScan from './ReceiptScan'

export default function Expenses({ me, members }: { me: Me; members: Member[] }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([])
  const [expenses, setExpenses] = useState<Expense[]>([])
  const [loading, setLoading] = useState(true)
  const [entryMode, setEntryMode] = useState<'manual' | 'receipt'>('manual')
  const isParent = me.role === 'Parent'

  const load = useCallback(() => {
    Promise.all([getCategories(), getPaymentMethods(), getExpenses()])
      .then(([c, p, e]) => {
        setCategories(c)
        setPaymentMethods(p)
        setExpenses(e)
      })
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  // Offer previously used value tags as suggestions while typing (SPEC feature 4) — gathered from
  // the expenses already loaded, so no extra endpoint is needed yet.
  const tagSuggestions = [
    ...new Set(expenses.flatMap((e) => e.lineItems.map((l) => l.valueTag).filter((t): t is string => !!t))),
  ].sort()

  const onSaved = (saved: Expense) => setExpenses((prev) => [saved, ...prev])

  if (loading) return <p>Loading…</p>

  return (
    <>
      {isParent ? (
        <section>
          <h2>Add an expense</h2>
          <div className="row entry-toggle">
            <button aria-pressed={entryMode === 'manual'} onClick={() => setEntryMode('manual')}>
              Enter manually
            </button>
            <button aria-pressed={entryMode === 'receipt'} onClick={() => setEntryMode('receipt')}>
              Scan a receipt
            </button>
          </div>
          {entryMode === 'manual' ? (
            <ExpenseForm
              categories={categories}
              paymentMethods={paymentMethods}
              members={members}
              tagSuggestions={tagSuggestions}
              onSaved={onSaved}
            />
          ) : (
            <ReceiptScan
              categories={categories}
              paymentMethods={paymentMethods}
              members={members}
              onSaved={onSaved}
            />
          )}
        </section>
      ) : (
        <p>Children can view expenses but not enter them.</p>
      )}

      <section>
        <h2>Recent expenses</h2>
        {expenses.length === 0 ? (
          <p>No expenses yet.</p>
        ) : (
          <ul className="expense-list">
            {expenses.map((e) => (
              <li key={e.id}>
                <div className="expense-head">
                  <strong>{e.merchant}</strong> · {e.date} · {e.paymentMethod} ·{' '}
                  <strong>{e.total.toFixed(2)}</strong>
                  {e.tax != null && <> (tax {e.tax.toFixed(2)})</>}
                </div>
                <ul className="line-list">
                  {e.lineItems.map((l) => (
                    <li key={l.id}>
                      {l.description} — {l.category} · for {l.for} · {l.amount.toFixed(2)}
                      {l.valueTag && <> · {l.valueTag}</>}
                      {l.shortForm && <span className="short-form"> [{l.shortForm}]</span>}
                      {l.notes && <div className="line-note">{l.notes}</div>}
                    </li>
                  ))}
                </ul>
                {e.notes && <div className="line-note">{e.notes}</div>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  )
}
