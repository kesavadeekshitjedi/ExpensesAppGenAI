import { useEffect, useState } from 'react'
import { getReportSummary, type Bucket, type ReportSummary } from '../api'

// First and last day of the current month, as YYYY-MM-DD, for the default range.
function monthRange(): { from: string; to: string } {
  const now = new Date()
  const first = new Date(now.getFullYear(), now.getMonth(), 1)
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  return { from: iso(first), to: iso(now) }
}

export default function Reports() {
  const initial = monthRange()
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [summary, setSummary] = useState<ReportSummary | null>(null)
  const [loading, setLoading] = useState(true)

  // Refetch whenever the range changes. State is updated only in the async callbacks (not
  // synchronously in the effect), and `active` guards against a stale response from a prior range.
  useEffect(() => {
    let active = true
    getReportSummary(from, to)
      .then((s) => active && setSummary(s))
      .catch(() => active && setSummary(null))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [from, to])

  return (
    <section>
      <h2>Reports</h2>
      <div className="row">
        <label>
          From
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>

      {loading && <p>Loading…</p>}

      {!loading && summary && (
        <>
          <p className="report-total">
            Total <strong>{summary.total.toFixed(2)}</strong> across {summary.lineItemCount} item
            {summary.lineItemCount === 1 ? '' : 's'}, {summary.from} to {summary.to}.
          </p>
          {summary.lineItemCount === 0 ? (
            <p>No spending in this period.</p>
          ) : (
            <div className="report-grid">
              <Breakdown title="By category" buckets={summary.byCategory} total={summary.total} />
              <Breakdown title="By who it was for" buckets={summary.byFor} total={summary.total} />
              <Breakdown title="By merchant" buckets={summary.byMerchant} total={summary.total} />
              <Breakdown title="By payment method" buckets={summary.byPaymentMethod} total={summary.total} />
              <Breakdown title="By item" buckets={summary.byItem} total={summary.total} />
              <Breakdown title="By value tag" buckets={summary.byValueTag} total={summary.total} />
            </div>
          )}
        </>
      )}
    </section>
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
