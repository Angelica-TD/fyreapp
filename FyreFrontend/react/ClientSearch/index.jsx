import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar, ToggleSwitch } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'

const columns = [
  { key: 'id',                   label: 'ID' },
  { key: 'name',                 label: 'Client' },
  { key: 'primaryContactName',   label: 'Primary contact' },
  { key: 'primaryContactMobile', label: 'Phone (BH)' },
  { key: 'siteCount',            label: 'No. of properties' },
  {
    key: 'actions',
    label: '',
    render: row => (
      <a
        href={`/Clients/Details/${row.id}`}
        className="btn btn-sm btn-outline-primary"
        onClick={e => e.stopPropagation()}
      >
        Details
      </a>
    )
  }
]

function ClientSearch() {
  const [query, setQuery]           = useState('')
  const [clients, setClients]       = useState([])
  const [loading, setLoading]       = useState(true)
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
      <SearchToolbar query={query} onQueryChange={setQuery} placeholder="Search by name or contact…">
        <ToggleSwitch
          id="showInactiveToggle"
          label="Show inactive"
          checked={showInactive}
          onChange={setShowInactive}
        />
      </SearchToolbar>
      <SearchTable
        columns={columns}
        rows={visible}
        loading={loading}
        onRowClick={row => window.location = `/Clients/Details/${row.id}`}
        emptyMessage="No clients found."
      />
    </>
  )
}

const el = document.getElementById('client-search-root')
if (el) createRoot(el).render(<ClientSearch />)
