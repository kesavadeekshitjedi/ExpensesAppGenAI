import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  getCategories,
  getItemPictureUrl,
  getItems,
  mergeItems,
  updateItem,
  uploadItemPicture,
  type Category,
  type Item,
  type Me,
} from '../api'

// The household item database (SPEC feature 3). Everyone can browse; parents can edit an item's name,
// default category and value tag, add a picture, and merge duplicates together.
export default function Items({ me }: { me: Me }) {
  const [items, setItems] = useState<Item[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const isParent = me.role === 'Parent'

  const load = useCallback((term: string) => {
    Promise.all([getItems(term), getCategories()])
      .then(([i, c]) => {
        setItems(i)
        setCategories(c)
      })
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [])

  // Refetch as the search term changes (debounced lightly by the input itself).
  useEffect(() => {
    const handle = setTimeout(() => load(search), 200)
    return () => clearTimeout(handle)
  }, [search, load])

  const tagSuggestions = useMemo(
    () => [...new Set(items.map((i) => i.defaultValueTag).filter((t): t is string => !!t))].sort(),
    [items],
  )

  return (
    <section>
      <h2>Item database</h2>
      <p className="hint">
        Items are built up automatically as you enter expenses and scan receipts. Each item remembers how it is
        printed on each store’s receipt, so it is recognized next time.
      </p>

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search items by name"
        aria-label="Search items"
      />

      {loading ? (
        <p>Loading…</p>
      ) : items.length === 0 ? (
        <p>{search ? 'No items match that search.' : 'No items yet — enter an expense or scan a receipt.'}</p>
      ) : (
        <ul className="item-list">
          {items.map((item) => (
            <ItemRow
              key={item.id}
              item={item}
              categories={categories}
              otherItems={items.filter((i) => i.id !== item.id)}
              isParent={isParent}
              onChanged={() => load(search)}
            />
          ))}
        </ul>
      )}

      <datalist id="item-value-tags">
        {tagSuggestions.map((t) => (
          <option key={t} value={t} />
        ))}
      </datalist>
    </section>
  )
}

function ItemRow({
  item,
  categories,
  otherItems,
  isParent,
  onChanged,
}: {
  item: Item
  categories: Category[]
  otherItems: Item[]
  isParent: boolean
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [pictureUrl, setPictureUrl] = useState<string | null>(null)

  // Load (and later revoke) the picture object URL once the item has one.
  useEffect(() => {
    if (!item.hasPicture) return
    let url: string | null = null
    getItemPictureUrl(item.id).then((u) => {
      url = u
      setPictureUrl(u)
    })
    return () => {
      if (url) URL.revokeObjectURL(url)
    }
  }, [item.id, item.hasPicture])

  return (
    <li className="item-row">
      <div className="item-main">
        {pictureUrl && <img className="item-thumb" src={pictureUrl} alt={item.fullName} />}
        <div className="grow">
          <strong>{item.fullName}</strong>
          <div className="hint">
            {item.defaultCategory ?? 'No default category'}
            {item.defaultValueTag && <> · {item.defaultValueTag}</>}
          </div>
          {item.receiptDescriptions.length > 0 && (
            <ul className="receipt-descs">
              {item.receiptDescriptions.map((d, i) => (
                <li key={i}>
                  {d.merchant}: <span className="short-form">{d.printedDescription}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
        {isParent && !editing && <button onClick={() => setEditing(true)}>Edit</button>}
      </div>

      {isParent && editing && (
        <EditItem
          item={item}
          categories={categories}
          otherItems={otherItems}
          onDone={() => {
            setEditing(false)
            onChanged()
          }}
          onCancel={() => setEditing(false)}
        />
      )}
    </li>
  )
}

function EditItem({
  item,
  categories,
  otherItems,
  onDone,
  onCancel,
}: {
  item: Item
  categories: Category[]
  otherItems: Item[]
  onDone: () => void
  onCancel: () => void
}) {
  const [fullName, setFullName] = useState(item.fullName)
  const [categoryId, setCategoryId] = useState(item.defaultCategoryId ?? '')
  const [valueTag, setValueTag] = useState(item.defaultValueTag ?? '')
  const [mergeTarget, setMergeTarget] = useState('')
  const [error, setError] = useState<string | null>(null)
  const activeCategories = categories.filter((c) => !c.archived || c.id === item.defaultCategoryId)

  const save = async () => {
    setError(null)
    if (fullName.trim() === '') {
      setError('Name is required.')
      return
    }
    const res = await updateItem(item.id, {
      fullName: fullName.trim(),
      defaultCategoryId: categoryId === '' ? null : categoryId,
      defaultValueTag: valueTag.trim() === '' ? null : valueTag.trim(),
    })
    if (res.ok) onDone()
    else setError('That name is already used by another item.')
  }

  const uploadPicture = async (file: File | undefined) => {
    if (!file) return
    setError(null)
    const res = await uploadItemPicture(item.id, file)
    if (res.ok) onDone()
    else setError('That file could not be stored as an image.')
  }

  const merge = async () => {
    if (mergeTarget === '') return
    setError(null)
    // This item is merged INTO the chosen target; this item then disappears.
    const res = await mergeItems(mergeTarget, item.id)
    if (res.ok) onDone()
    else setError('Could not merge those items.')
  }

  return (
    <div className="item-edit">
      <label className="grow">
        Full name
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} />
      </label>
      <div className="row">
        <label>
          Default category
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            <option value="">— none —</option>
            {activeCategories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Default value tag
          <input
            list="item-value-tags"
            value={valueTag}
            onChange={(e) => setValueTag(e.target.value)}
            placeholder="e.g. Needed"
          />
        </label>
      </div>

      <label>
        Picture of the item
        <input type="file" accept="image/*" onChange={(e) => uploadPicture(e.target.files?.[0])} />
      </label>

      {otherItems.length > 0 && (
        <div className="row">
          <label className="grow">
            Merge this item into
            <select value={mergeTarget} onChange={(e) => setMergeTarget(e.target.value)}>
              <option value="">— choose an item —</option>
              {otherItems.map((i) => (
                <option key={i.id} value={i.id}>
                  {i.fullName}
                </option>
              ))}
            </select>
          </label>
          <button type="button" onClick={merge} disabled={mergeTarget === ''}>
            Merge
          </button>
        </div>
      )}

      {error && <p role="alert">{error}</p>}
      <div className="row">
        <button type="button" onClick={save}>
          Save
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  )
}
