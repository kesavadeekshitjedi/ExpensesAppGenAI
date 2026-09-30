import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiFetch, type Invitation, type Me, type Member } from '../api'

export default function Dashboard({ me, onSignOut }: { me: Me; onSignOut: () => void }) {
  const [members, setMembers] = useState<Member[]>([])
  const [invitations, setInvitations] = useState<Invitation[]>([])
  const isParent = me.role === 'Parent'

  const loadMembers = useCallback(() => {
    apiFetch('/members')
      .then((r) => r.json() as Promise<Member[]>)
      .then(setMembers)
      .catch(() => {})
  }, [])

  const loadInvitations = useCallback(() => {
    if (!isParent) return
    apiFetch('/invitations')
      .then((r) => r.json() as Promise<Invitation[]>)
      .then(setInvitations)
      .catch(() => {})
  }, [isParent])

  useEffect(() => {
    loadMembers()
    loadInvitations()
  }, [loadMembers, loadInvitations])

  return (
    <main>
      <header>
        <h1>Home Expenses</h1>
        <p>
          Signed in as <strong>{me.displayName}</strong> ({me.role}){' '}
          <button onClick={onSignOut}>Sign out</button>
        </p>
      </header>

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
        {isParent && <AddMember onAdded={loadMembers} />}
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
    </main>
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
