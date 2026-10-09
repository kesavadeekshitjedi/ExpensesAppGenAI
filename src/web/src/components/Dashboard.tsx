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

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    await apiFetch('/members', {
      method: 'POST',
      body: JSON.stringify({ displayName: name.trim(), role: 'Child' }),
    })
    setName('')
    onAdded()
  }

  return (
    <form onSubmit={submit}>
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Add a child (name)" />
      <button type="submit">Add</button>
    </form>
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
