import { useState } from 'react'
import type { Category } from '../api'

// A category <select> with an inline "➕ Add new category…" option, so a parent can create a category
// (e.g. "Vegetables") without leaving the form. Picking the add option swaps the control for a small text
// input; on Add it creates the category, selects it, and returns to the dropdown. `categories` should be
// the active (non-archived) list; `onAddCategory` creates one and resolves to the created category.
export default function CategorySelect({
  value,
  categories,
  onChange,
  onAddCategory,
}: {
  value: string
  categories: Category[]
  onChange: (categoryId: string) => void
  onAddCategory: (name: string) => Promise<Category>
}) {
  const [adding, setAdding] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const confirmAdd = async () => {
    const trimmed = name.trim()
    if (trimmed === '') {
      setAdding(false)
      return
    }
    setBusy(true)
    setError(null)
    try {
      const created = await onAddCategory(trimmed)
      onChange(created.id)
      setAdding(false)
      setName('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add category.')
    } finally {
      setBusy(false)
    }
  }

  if (adding) {
    return (
      <span className="category-add">
        <input
          autoFocus
          value={name}
          disabled={busy}
          placeholder="New category"
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              void confirmAdd()
            } else if (e.key === 'Escape') {
              setAdding(false)
              setName('')
            }
          }}
        />
        <button type="button" onClick={() => void confirmAdd()} disabled={busy}>
          {busy ? '…' : 'Add'}
        </button>
        <button type="button" onClick={() => { setAdding(false); setName('') }} disabled={busy}>
          ✕
        </button>
        {error && <span role="alert" className="hint">{error}</span>}
      </span>
    )
  }

  return (
    <select
      value={value}
      onChange={(e) => {
        if (e.target.value === '__add__') {
          setAdding(true)
          return
        }
        onChange(e.target.value)
      }}
    >
      {categories.map((c) => (
        <option key={c.id} value={c.id}>
          {c.name}
        </option>
      ))}
      <option value="__add__">➕ Add new category…</option>
    </select>
  )
}
