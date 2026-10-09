import { useEffect, useMemo, useState } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import {
  getItemPriceHistory,
  getItems,
  getReportSummary,
  getTrend,
  type Bucket,
  type ItemPriceHistory,
  type ReportSummary,
  type TrendInterval,
  type TrendResponse,
} from '../api'

// Default to the last 6 months so the trend chart is meaningful out of the box.
function defaultRange(): { from: string; to: string } {
  const now = new Date()
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  return { from: iso(new Date(now.getFullYear(), now.getMonth() - 5, 1)), to: iso(now) }
}

const BRAND = '#863bff'
const PALETTE = ['#863bff', '#14b8a6', '#f59e0b', '#ef4444', '#3b82f6', '#ec4899', '#22c55e', '#a855f7']
const AXIS = '#8b93a3'
const GRID = 'rgba(136,136,160,0.2)'

const money = (n: number) => `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const axisMoney = (n: number) => (n >= 1000 ? `$${(n / 1000).toFixed(1)}k` : `$${n}`)

function formatPeriod(period: string, interval: string): string {
  if (interval === 'month') {
    const [y, m] = period.split('-').map(Number)
    return new Date(y, m - 1, 1).toLocaleString(undefined, { month: 'short' })
  }
  const d = new Date(period)
  return `${d.getMonth() + 1}/${d.getDate()}`
}

export default function Reports() {
  const initial = defaultRange()
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [interval, setInterval] = useState<TrendInterval>('month')

  const [summary, setSummary] = useState<ReportSummary | null>(null)
  const [trend, setTrend] = useState<TrendResponse | null>(null)

  useEffect(() => {
    let active = true
    Promise.all([getReportSummary(from, to), getTrend(from, to, interval)])
      .then(([s, t]) => {
        if (!active) return
        setSummary(s)
        setTrend(t)
      })
      .catch(() => active && setSummary(null))
    return () => {
      active = false
    }
  }, [from, to, interval])

  return (
    <section>
      <h2>Reports</h2>
      <div className="row report-controls">
        <label>
          From
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <div className="row entry-toggle" role="group" aria-label="Interval">
          {(['day', 'week', 'month'] as TrendInterval[]).map((iv) => (
            <button key={iv} aria-pressed={interval === iv} onClick={() => setInterval(iv)}>
              {iv[0].toUpperCase() + iv.slice(1)}
            </button>
          ))}
        </div>
      </div>

      {summary === null && <p>Loading…</p>}

      {summary && (
        <>
          <p className="report-total">
            <strong>{money(summary.total)}</strong> across {summary.lineItemCount} item
            {summary.lineItemCount === 1 ? '' : 's'}, {summary.from} to {summary.to}.
          </p>

          <div className="chart-card">
            <h3>Spending over time</h3>
            {trend && trend.points.some((p) => p.total > 0) ? (
              <ResponsiveContainer width="100%" height={260}>
                <AreaChart data={trend.points} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
                  <defs>
                    <linearGradient id="spendFill" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={BRAND} stopOpacity={0.5} />
                      <stop offset="100%" stopColor={BRAND} stopOpacity={0.04} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid stroke={GRID} vertical={false} />
                  <XAxis
                    dataKey="period"
                    tickFormatter={(p) => formatPeriod(p, trend.interval)}
                    tick={{ fill: AXIS, fontSize: 12 }}
                    tickLine={false}
                    axisLine={{ stroke: GRID }}
                  />
                  <YAxis tickFormatter={axisMoney} tick={{ fill: AXIS, fontSize: 12 }} tickLine={false} axisLine={false} width={48} />
                  <Tooltip
                    formatter={(v) => [money(Number(v)), 'Spent']}
                    labelFormatter={(p) => formatPeriod(String(p), trend.interval)}
                    contentStyle={{ borderRadius: 8, border: '1px solid #8884', fontSize: 13 }}
                  />
                  <Area type="monotone" dataKey="total" stroke={BRAND} strokeWidth={2} fill="url(#spendFill)" />
                </AreaChart>
              </ResponsiveContainer>
            ) : (
              <p className="hint">No spending in this period.</p>
            )}
          </div>

          <div className="chart-card">
            <h3>By category</h3>
            <CategoryBars buckets={summary.byCategory} />
          </div>

          <ItemPriceTrend from={from} to={to} />

          {summary.lineItemCount > 0 && (
            <div className="report-grid">
              <Breakdown title="By who it was for" buckets={summary.byFor} total={summary.total} />
              <Breakdown title="By merchant" buckets={summary.byMerchant} total={summary.total} />
              <Breakdown title="By payment method" buckets={summary.byPaymentMethod} total={summary.total} />
              <Breakdown title="By vehicle" buckets={summary.byVehicle} total={summary.total} />
              <Breakdown title="By value tag" buckets={summary.byValueTag} total={summary.total} />
              <Breakdown title="By item" buckets={summary.byItem} total={summary.total} />
            </div>
          )}
        </>
      )}
    </section>
  )
}

function CategoryBars({ buckets }: { buckets: Bucket[] }) {
  const data = buckets.slice(0, 8)
  if (data.length === 0) return <p className="hint">No spending in this period.</p>
  return (
    <ResponsiveContainer width="100%" height={Math.max(140, data.length * 38)}>
      <BarChart data={data} layout="vertical" margin={{ top: 4, right: 16, left: 8, bottom: 4 }}>
        <CartesianGrid stroke={GRID} horizontal={false} />
        <XAxis type="number" tickFormatter={axisMoney} tick={{ fill: AXIS, fontSize: 12 }} tickLine={false} axisLine={{ stroke: GRID }} />
        <YAxis type="category" dataKey="key" width={110} tick={{ fill: AXIS, fontSize: 12 }} tickLine={false} axisLine={false} />
        <Tooltip formatter={(v) => [money(Number(v)), 'Spent']} cursor={{ fill: 'rgba(136,136,160,0.08)' }} contentStyle={{ borderRadius: 8, border: '1px solid #8884', fontSize: 13 }} />
        <Bar dataKey="total" radius={[0, 6, 6, 0]}>
          {data.map((_, i) => (
            <Cell key={i} fill={PALETTE[i % PALETTE.length]} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

function ItemPriceTrend({ from, to }: { from: string; to: string }) {
  const [items, setItems] = useState<{ id: string; fullName: string }[]>([])
  const [itemId, setItemId] = useState('')
  const [history, setHistory] = useState<ItemPriceHistory | null>(null)

  useEffect(() => {
    getItems()
      .then((list) => setItems(list.map((i) => ({ id: i.id, fullName: i.fullName }))))
      .catch(() => {})
  }, [])

  useEffect(() => {
    if (!itemId) return
    let active = true
    getItemPriceHistory(itemId, from, to)
      .then((h) => active && setHistory(h))
      .catch(() => active && setHistory(null))
    return () => {
      active = false
    }
  }, [itemId, from, to])

  // Plot unit price when the item has one; otherwise fall back to the line amount.
  const { data, key } = useMemo(() => {
    const pts = history?.points ?? []
    const useUnit = pts.some((p) => p.unitPrice > 0)
    return {
      key: useUnit ? 'unitPrice' : 'amount',
      data: pts.map((p) => ({ date: p.date, unitPrice: p.unitPrice, amount: p.amount, merchant: p.merchant })),
    }
  }, [history])

  return (
    <div className="chart-card">
      <h3>Item price trend</h3>
      <label className="price-trend-pick">
        Item
        <select value={itemId} onChange={(e) => setItemId(e.target.value)}>
          <option value="">Choose an item…</option>
          {items.map((i) => (
            <option key={i.id} value={i.id}>
              {i.fullName}
            </option>
          ))}
        </select>
      </label>

      {!itemId ? (
        <p className="hint">Pick an item to see how its price changed over time.</p>
      ) : data.length === 0 ? (
        <p className="hint">No price history for this item in the selected period.</p>
      ) : (
        <ResponsiveContainer width="100%" height={240}>
          <LineChart data={data} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
            <CartesianGrid stroke={GRID} vertical={false} />
            <XAxis dataKey="date" tickFormatter={(d) => formatPeriod(d, 'day')} tick={{ fill: AXIS, fontSize: 12 }} tickLine={false} axisLine={{ stroke: GRID }} />
            <YAxis tickFormatter={axisMoney} tick={{ fill: AXIS, fontSize: 12 }} tickLine={false} axisLine={false} width={48} domain={['auto', 'auto']} />
            <Tooltip
              formatter={(v) => [money(Number(v)), key === 'unitPrice' ? 'Unit price' : 'Amount']}
              labelFormatter={(d) => String(d)}
              contentStyle={{ borderRadius: 8, border: '1px solid #8884', fontSize: 13 }}
            />
            <Line type="monotone" dataKey={key} stroke={BRAND} strokeWidth={2} dot={{ r: 3, fill: BRAND }} activeDot={{ r: 5 }} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  )
}

function Breakdown({ title, buckets, total }: { title: string; buckets: Bucket[]; total: number }) {
  return (
    <div className="breakdown">
      <h3>{title}</h3>
      {buckets.length === 0 ? (
        <p className="hint">None.</p>
      ) : (
        <table>
          <tbody>
            {buckets.map((b) => (
              <tr key={b.key}>
                <td>{b.key}</td>
                <td className="amount">{b.total.toFixed(2)}</td>
                <td className="share">{total > 0 ? Math.round((b.total / total) * 100) : 0}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
