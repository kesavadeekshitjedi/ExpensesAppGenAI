import { useCallback, useEffect, useState } from 'react'
import {
  getExpense,
  searchExpenses,
  type Category,
  type Expense,
  type ExpenseSearchLine,
  type Member,
  type PaymentMethod,
  type Vehicle,
} from '../api'
import ExpenseForm from './ExpenseForm'

const iso = (d: Date) => d.toISOString().slice(0, 10)
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86_400_000))

// Find an item/expense (default: last 30 days) and open it for editing. Lets the user fix a wrong import
// or a mistyped entry: search by item, description, or store, see the matching lines with date / amount /
// store, then open the full expense (merchant, payment method, all lines) to edit or delete.
export default function FindExpenses({
  categories,
  paymentMethods,
  members,
  vehicles,
  tagSuggestions,
  merchantSuggestions,
  itemSuggestions,
  onSaved,
  onDeleted,
  onAddCategory,
}: {
  categories: Category[]
  paymentMethods: PaymentMethod[]
  members: Member[]
  vehicles: Vehicle[]
  tagSuggestions: string[]
  merchantSuggestions: string[]
  itemSuggestions: string[]
  onSaved: (expense: Expense) => void
  onDeleted: (id: string) => void
  onAddCategory: (name: string) => Promise<Category>
}) {
  const [q, setQ] = useState('')
  const [from, setFrom] = useState(daysAgo(30))
  const [to, setTo] = useState(iso(new Date()))
  const [results, setResults] = useState<ExpenseSearchLine[]>([])
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<Expense | null>(null)

  const runSearch = useCallback(async () => {
    setSearching(true)
    setError(null)
    try {
      setResults(await searchExpenses(q, from, to))
    } catch {
      setError('Could not search. Try again.')
    } finally {
      setSearching(false)
    }
  }, [q, from, to])

  // Show the most recent 30 days on open so there's something to scan without typing.
  useEffect(() => {
    void runSearch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const openForEdit = async (expenseId: string) => {
    setError(null)
    try {
      setSelected(await getExpense(expenseId))
    } catch {
      setError('Could not open that expense.')
    }
  }

  const handleSaved = (saved: Expense) => {
    onSaved(saved)
    setSelected(null)
    void runSearch()
  }

  const handleDeleted = (id: string) => {
    onDeleted(id)
    setSelected(null)
    void runSearch()
  }

  if (selected) {
    return (
      <div>
        <div className="row">
          <button type="button" onClick={() => setSelected(null)}>
            ← Back to results
          </button>
          <span className="hint">
            Editing {selected.merchant} · {selected.date} · {selected.total.toFixed(2)} ({selected.source.toLowerCase()})
          </span>
        </div>
        {/* key remounts the form so it re-initializes if a different expense is opened */}
        <ExpenseForm
          key={selected.id}
          categories={categories}
          paymentMethods={paymentMethods}
          members={members}
          vehicles={vehicles}
          tagSuggestions={tagSuggestions}
          merchantSuggestions={merchantSuggestions}
          itemSuggestions={itemSuggestions}
          onSaved={handleSaved}
          onAddCategory={onAddCategory}
          initial={selected}
          onDeleted={handleDeleted}
        />
      </div>
    )
  }

  return (
    <div>
      <form
        className="row"
        onSubmit={(e) => {
          e.preventDefault()
          void runSearch()
        }}
      >
        <label className="grow">
          Search
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="item, description, or store — e.g. Dosakai"
          />
        </label>
        <label>
          From
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button type="submit" disabled={searching}>
          {searching ? 'Searching…' : 'Search'}
        </button>
      </form>

      {error && <p role="alert">{error}</p>}

      {results.length === 0 ? (
        <p className="hint">{searching ? 'Searching…' : 'No matching items in this date range.'}</p>
      ) : (
        <table className="search-results">
          <thead>
            <tr>
              <th>Date</th>
              <th>Item</th>
              <th>Store</th>
              <th className="num">Amount</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {results.map((r) => (
              <tr key={r.lineItemId}>
                <td>{r.date}</td>
                <td>
                  {r.itemFullName ?? r.description}
                  <span className="hint"> · {r.category}</span>
                </td>
                <td>
                  {r.merchant}
                  <span className="hint"> · {r.paymentMethod}</span>
                </td>
                <td className="num">{r.amount.toFixed(2)}</td>
                <td>
                  <button type="button" onClick={() => openForEdit(r.expenseId)}>
                    Edit
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
