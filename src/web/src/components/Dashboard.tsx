import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiFetch, type Invitation, type Me, type Member } from '../api'
import Expenses from './Expenses'
import Items from './Items'
import Reports from './Reports'
import Settings from './Settings'

type Tab = 'expenses' | 'items' | 'reports' | 'settings' | 'household'

export default function Dashboard({ me, onSignOut }: { me: Me; onSignOut: () => void }) {
  const [tab, setTab] = useState<Tab>('expenses')
  const [members, setMembers] = useState<Member[]>([])

  // Members are loaded here because both the Household tab and the expense "for" picker need them.
  const loadMembers = useCallback(() => {
    apiFetch('/members')
      .then((r) => r.json() as Promise<Member[]>)
      .then(setMembers)
      .catch(() => {})
  }, [])

  useEffect(loadMembers, [loadMembers])

  return (
    <main>
      <header>
        <h1>Home Expenses</h1>
        <p>
          Signed in as <strong>{me.displayName}</strong> ({me.role}){' '}
          <button onClick={onSignOut}>Sign out</button>
        </p>
        <nav className="tabs">
          <button aria-current={tab === 'expenses'} onClick={() => setTab('expenses')}>
            Expenses
          </button>
          <button aria-current={tab === 'items'} onClick={() => setTab('items')}>
            Items
          </button>
          <button aria-current={tab === 'reports'} onClick={() => setTab('reports')}>
            Reports
          </button>
          <button aria-current={tab === 'settings'} onClick={() => setTab('settings')}>
            Settings
          </button>
          <button aria-current={tab === 'household'} onClick={() => setTab('household')}>
            Household
          </button>
        </nav>
      </header>

      {tab === 'expenses' && <Expenses me={me} members={members} />}
      {tab === 'items' && <Items me={me} />}
      {tab === 'reports' && <Reports />}
      {tab === 'settings' && <Settings me={me} />}
      {tab === 'household' && <Household me={me} members={members} onMembersChanged={loadMembers} />}
    </main>
  )
}

function Household({ me, members, onMembersChanged }: { me: Me; members: Member[]; onMembersChanged: () => void }) {
  const [invitations, setInvitations] = useState<Invitation[]>([])
  const isParent = me.role === 'Parent'

  const loadInvitations = useCallback(() => {
    if (!isParent) return
    apiFetch('/invitations')
      .then((r) => r.json() as Promise<Invitation[]>)
      .then(setInvitations)
      .catch(() => {})
  }, [isParent])

  useEffect(loadInvitations, [loadInvitations])

  return (
    <>
      <section>
        <h2>Members</h2>
        <ul>
          {members.map((m) => (
            <li key={m.id}>
              {m.displayName} — {m.role}
              {m.canSignIn ? '' : ' (no sign-in)'}
              {isParent && !m.canSignIn && (
                <>
                  {' '}
                  <InviteMember member={m} onInvited={loadInvitations} />{' '}
                  <RemoveMember member={m} onRemoved={onMembersChanged} />
                </>
              )}
            </li>
          ))}
        </ul>
        {isParent && <AddMember onAdded={onMembersChanged} />}
      </section>

      {isParent && (
        <section>
          <h2>Invitations</h2>
          <CreateInvitation onCreated={loadInvitations} />
          <ul>
            {invitations.map((i) => (
              <li key={i.id}>
                {i.role} · {i.status} · expires {new Date(i.expiresAt).toLocaleDateString()}
                {i.status === 'Pending' && (
                  <>
                    {' '}
                    <InviteLink code={i.code} />{' '}
                    <button onClick={() => revoke(i.id, loadInvitations)}>Revoke</button>
                  </>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

async function revoke(id: string, reload: () => void) {
  await apiFetch(`/invitations/${id}/revoke`, { method: 'POST' })
  reload()
}

function InviteLink({ code }: { code: string }) {
  const link = `${window.location.origin}/?invite=${encodeURIComponent(code)}`
  return <a href={link}>invite link</a>
}

function AddMember({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('')
  // A member can be an adult (full access once they sign in) or a child (view-only). Neither signs in
  // until invited; the role just decides their access if they later accept an invitation.
  const [role, setRole] = useState('Child')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!name.trim() || busy) return
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch('/members', {
        method: 'POST',
        body: JSON.stringify({ displayName: name.trim(), role }),
      })
      if (!res.ok) {
        setError('Could not add that member. Please try again.')
        return
      }
      setName('')
      onAdded()
    } catch {
      setError('Could not add that member. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <input
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Add a member (name)"
        disabled={busy}
      />
      <select value={role} onChange={(e) => setRole(e.target.value)} disabled={busy} aria-label="Member type">
        <option value="Child">Child (view-only)</option>
        <option value="Parent">Adult (full access)</option>
      </select>
      <button type="submit" disabled={busy || !name.trim()}>
        {busy ? 'Adding…' : 'Add'}
      </button>
      {error && <span role="alert" className="hint">{error}</span>}
    </form>
  )
}

function InviteMember({ member, onInvited }: { member: Member; onInvited: () => void }) {
  const [busy, setBusy] = useState(false)
  const [code, setCode] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const invite = async () => {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      // Target this existing member so accepting the invite attaches sign-in to them (no duplicate).
      // They keep the role they were added with (adult = full access, child = view-only).
      const res = await apiFetch('/invitations', {
        method: 'POST',
        body: JSON.stringify({ role: member.role, memberId: member.id }),
      })
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { message?: string } | null
        setError(body?.message ?? 'Could not create an invitation.')
        return
      }
      const inv = (await res.json()) as Invitation
      setCode(inv.code)
      onInvited()
    } catch {
      setError('Could not create an invitation.')
    } finally {
      setBusy(false)
    }
  }

  if (code) {
    return (
      <>
        <InviteLink code={code} /> <span className="hint">— send this to {member.displayName}</span>
      </>
    )
  }

  return (
    <>
      <button type="button" onClick={invite} disabled={busy}>
        {busy ? 'Inviting…' : 'Invite to sign in'}
      </button>
      {error && <span role="alert" className="hint"> {error}</span>}
    </>
  )
}

function RemoveMember({ member, onRemoved }: { member: Member; onRemoved: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const remove = async () => {
    if (busy) return
    if (!window.confirm(`Remove ${member.displayName}? This can't be undone.`)) return
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch(`/members/${member.id}`, { method: 'DELETE' })
      if (res.ok) {
        onRemoved()
        return
      }
      const body = (await res.json().catch(() => null)) as { message?: string } | null
      setError(body?.message ?? 'Could not remove that member.')
    } catch {
      setError('Could not remove that member.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button type="button" onClick={remove} disabled={busy}>
        {busy ? 'Removing…' : 'Remove'}
      </button>
      {error && <span role="alert" className="hint"> {error}</span>}
    </>
  )
}

function CreateInvitation({ onCreated }: { onCreated: () => void }) {
  const [role, setRole] = useState('Parent')

  const create = async () => {
    await apiFetch('/invitations', { method: 'POST', body: JSON.stringify({ role }) })
    onCreated()
  }

  return (
    <div>
      <select value={role} onChange={(e) => setRole(e.target.value)}>
        <option value="Parent">Parent</option>
        <option value="Child">Child</option>
      </select>
      <button onClick={create}>Create invitation</button>
    </div>
  )
}
