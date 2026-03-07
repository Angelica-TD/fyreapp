import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'

function SearchToolbar({ query, onQueryChange, showInactive, onShowInactiveChange }) {
  return (
    <div className="d-flex gap-2 mb-3 mt-3">
      <input
        type="search"
        className="form-control"
        placeholder="Search by name or contact…"
        value={query}
        onChange={e => onQueryChange(e.target.value)}
      />
      <div className="form-check form-switch d-flex align-items-center ms-2 text-nowrap">
        <input
          className="form-check-input me-2"
          type="checkbox"
          id="showInactiveToggle"
          checked={showInactive}
          onChange={e => onShowInactiveChange(e.target.checked)}
        />
        <label className="form-check-label" htmlFor="showInactiveToggle">
          Show inactive
        </label>
      </div>
    </div>
  )
}

function ClientTable({ clients }) {
  return (
    <table className="table table-hover align-middle">
      <thead>
        <tr>
          <th>ID</th>
          <th>Client</th>
          <th>Primary contact</th>
          <th>Phone (BH)</th>
          <th>No. of properties</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        {clients.length === 0 ? (
          <tr>
            <td colSpan={6} className="text-muted">No clients found.</td>
          </tr>
        ) : clients.map(c => (
          <tr
            key={c.id}
            style={{ cursor: 'pointer' }}
            onClick={() => window.location = `/Clients/Details/${c.id}`}
          >
            <td>{c.id}</td>
            <td>{c.name}</td>
            <td>{c.primaryContactName}</td>
            <td>{c.primaryContactMobile}</td>
            <td>{c.siteCount}</td>
            <td>
                <a href={`/Clients/Details/${c.id}`}
                    className="btn btn-sm btn-outline-primary"
                    onClick={e => e.stopPropagation()}
                >
                Details
                </a>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function ClientSearch() {
  const [query, setQuery] = useState('')
  const [clients, setClients] = useState([])
  const [loading, setLoading] = useState(true)
  const [showInactive, setShowInactive] = useState(false)

  useEffect(() => {
    setLoading(true)
    fetch(`/api/clients?search=${encodeURIComponent(query)}`)
      .then(res => res.json())
      .then(data => { setClients(data); setLoading(false) })
  }, [query])

  const visible = showInactive ? clients : clients.filter(c => c.active !== false)

  return (
    <>
      <SearchToolbar
        query={query}
        onQueryChange={setQuery}
        showInactive={showInactive}
        onShowInactiveChange={setShowInactive}
      />
      {loading ? (
        <p className="text-muted">Loading…</p>
      ) : (
        <ClientTable clients={visible} />
      )}
    </>
  )
}

const el = document.getElementById('client-search-root')
if (el) createRoot(el).render(<ClientSearch />)