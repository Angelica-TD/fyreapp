import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'
import { FiltersButton, FilterPanel, FilterRow, YesNoAny, ListPager } from '../shared/ListControls'

// Defaults match Uptick's Clients page: active clients only
const DEFAULT_ACTIVE = 'yes'

const columns = [
  { key: 'displayRef', label: 'Ref', render: row => row.displayRef ?? '—' },
  {
    key: 'name',
    label: 'Client',
    render: row => (
      <>
        <a href={`/Clients/Details/${row.id}`} onClick={e => e.stopPropagation()}>{row.name}</a>
        {row.primaryContactName && <span className="text-muted"> · {row.primaryContactName}</span>}
      </>
    )
  },
  { key: 'primaryContactMobile', label: 'Mobile', render: row => row.primaryContactMobile ?? '—' },
  { key: 'siteCount', label: 'Properties' },
  {
    key: 'active',
    label: 'Active',
    render: row => row.active
      ? <i className="bi bi-check-circle-fill text-success" title="Active"></i>
      : <i className="bi bi-x-circle-fill text-danger" title="Inactive"></i>
  }
]

function ClientSearch() {
  const [query, setQuery]       = useState('')
  const [active, setActive]     = useState(DEFAULT_ACTIVE)
  const [page, setPage]         = useState(1)
  const [showFilters, setShowFilters] = useState(true)
  const [data, setData]         = useState({ total: 0, pages: 1, items: [] })
  const [loading, setLoading]   = useState(true)

  // A new search or filter starts again at page 1
  useEffect(() => { setPage(1) }, [query, active])

  useEffect(() => {
    let stale = false // ignore a slower, older response
    setLoading(true)
    const params = new URLSearchParams({ search: query, active, page })
    fetch(`/api/clients?${params}`)
      .then(res => res.json())
      .then(json => { if (!stale) { setData(json); setLoading(false) } })
    return () => { stale = true }
  }, [query, active, page])

  return (
    <>
      <SearchToolbar query={query} onQueryChange={setQuery} placeholder="Search by name, contact or ref…">
        <FiltersButton count={active !== 'any' ? 1 : 0} open={showFilters} onToggle={() => setShowFilters(!showFilters)} />
      </SearchToolbar>

      <FilterPanel open={showFilters} onReset={() => setActive(DEFAULT_ACTIVE)}>
        <FilterRow label="Active" htmlFor="clientActive">
          <YesNoAny id="clientActive" value={active} onChange={setActive} />
        </FilterRow>
      </FilterPanel>

      <p className="text-muted mb-2">{data.total.toLocaleString()} client{data.total === 1 ? '' : 's'}</p>
      <SearchTable
        columns={columns}
        rows={data.items}
        loading={loading}
        onRowClick={row => window.location = `/Clients/Details/${row.id}`}
        emptyMessage="No clients found."
      />
      <ListPager page={data.page ?? page} pages={data.pages} onPage={setPage} />
    </>
  )
}

const el = document.getElementById('client-search-root')
if (el) createRoot(el).render(<ClientSearch />)
