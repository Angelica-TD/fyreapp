import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'
import {
  FiltersButton, FilterPanel, FilterRow, YesNoAny, ListPager,
  useSelection, selectColumn, SelectionBar, Modal, postJson, SuccessAlert
} from '../shared/ListControls'

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

function ClientSearch({ canEdit }) {
  const [query, setQuery]       = useState('')
  const [active, setActive]     = useState(DEFAULT_ACTIVE)
  const [page, setPage]         = useState(1)
  const [showFilters, setShowFilters] = useState(true)
  const [data, setData]         = useState({ total: 0, pages: 1, items: [] })
  const [loading, setLoading]   = useState(true)
  const [reload, setReload]     = useState(0)
  const [editing, setEditing]   = useState(false)
  const [bulkAction, setBulkAction] = useState('deactivate')
  const [busy, setBusy]         = useState(false)
  const [message, setMessage]   = useState(null)

  const selection = useSelection(data.items, data.total, `${query}|${active}|${page}|${reload}`)

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
  }, [query, active, page, reload])

  const filterParams = new URLSearchParams({ search: query, active })

  async function apply() {
    setBusy(true)
    try {
      const res = await postJson('/api/clients/bulk', {
        ids: selection.ids, allMatching: selection.allMatching, search: query, active, bulkAction
      })
      setMessage(res.message)
      setEditing(false)
      setReload(r => r + 1)
    } catch (err) {
      alert(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <SuccessAlert message={message} onClose={() => setMessage(null)} />

      <SearchToolbar query={query} onQueryChange={setQuery} placeholder="Search by name, contact or ref…">
        <FiltersButton count={active !== 'any' ? 1 : 0} open={showFilters} onToggle={() => setShowFilters(!showFilters)} />
      </SearchToolbar>

      <FilterPanel open={showFilters} onReset={() => setActive(DEFAULT_ACTIVE)}>
        <FilterRow label="Active" htmlFor="clientActive">
          <YesNoAny id="clientActive" value={active} onChange={setActive} />
        </FilterRow>
      </FilterPanel>

      <SelectionBar selection={selection} total={data.total} noun="Clients" nounSingular="Client" canEdit={canEdit}
                    onEdit={() => setEditing(true)} downloadUrl={`/Clients/Download?${filterParams}`} />

      <SearchTable
        columns={canEdit ? [selectColumn(selection), ...columns] : columns}
        rows={data.items}
        loading={loading}
        onRowClick={row => window.location = `/Clients/Details/${row.id}`}
        emptyMessage="No clients found."
      />
      <ListPager page={data.page ?? page} pages={data.pages} onPage={setPage} />

      <Modal title={`Edit ${selection.count.toLocaleString()} Clients`} open={editing} busy={busy}
             onClose={() => setEditing(false)} onSubmit={apply}>
        <p>You have selected {selection.count.toLocaleString()} clients. What do you want to do?</p>
        {[['deactivate', 'Set inactive'], ['activate', 'Set active']].map(([value, label]) => (
          <div className="form-check" key={value}>
            <input className="form-check-input" type="radio" name="clientBulkAction" id={`client-${value}`}
                   checked={bulkAction === value} onChange={() => setBulkAction(value)} />
            <label className="form-check-label" htmlFor={`client-${value}`}>{label}</label>
          </div>
        ))}
      </Modal>
    </>
  )
}

const el = document.getElementById('client-search-root')
if (el) createRoot(el).render(<ClientSearch canEdit={el.dataset.canEdit === 'true'} />)
