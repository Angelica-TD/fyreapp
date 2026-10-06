import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'
import { FiltersButton, FilterPanel, FilterRow, YesNoAny, IsCheckboxes, ListPager } from '../shared/ListControls'

const STATUSES = ['Open', 'InProgress', 'Blocked', 'Completed', 'Cancelled']
const statusLabel = s => s === 'InProgress' ? 'In Progress' : s

const STATUS_BADGES = {
  Open:       'bg-info text-dark',
  InProgress: 'bg-primary',
  Blocked:    'bg-warning text-dark',
  Completed:  'bg-success',
  Cancelled:  'bg-secondary',
}

// Defaults match Uptick's Tasks page: active tasks, any category, any status
const DEFAULTS = { active: 'yes', category: [], categoryNot: false, status: [], statusNot: false }

function DueDate({ iso, status }) {
  if (!iso) return <span className="text-muted">—</span>
  const due = new Date(iso)
  const overdue = due < new Date()
    && status !== 'Completed'
    && status !== 'Cancelled'
  return (
    <span className={overdue ? 'text-danger fw-semibold' : ''}>
      {due.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })}
    </span>
  )
}

const columns = [
  { key: 'displayRef', label: 'Ref', render: row => <a href={`/Task/Details/${row.id}`} onClick={e => e.stopPropagation()}>{row.displayRef ?? '—'}</a> },
  {
    key: 'category',
    label: 'Category',
    render: row => row.category ? <span className="badge bg-dark">{row.category.toUpperCase()}</span> : <span className="text-muted">—</span>
  },
  {
    key: 'status',
    label: 'Status',
    render: row => (
      <span className={`badge ${STATUS_BADGES[row.status] ?? 'bg-secondary'}`}>{statusLabel(row.status).toUpperCase()}</span>
    )
  },
  { key: 'title',       label: 'Task' },
  { key: 'clientName',  label: 'Client' },
  { key: 'siteAddress', label: 'Property' },
  { key: 'dueDateUtc',  label: 'Due', render: row => <DueDate iso={row.dueDateUtc} status={row.status} /> }
]

function TaskSearch() {
  const [query, setQuery]     = useState('')
  const [filters, setFilters] = useState(DEFAULTS)
  const [page, setPage]       = useState(1)
  const [showFilters, setShowFilters] = useState(true)
  const [data, setData]       = useState({ total: 0, pages: 1, items: [], categories: [] })
  const [loading, setLoading] = useState(true)

  const set = patch => setFilters(f => ({ ...f, ...patch }))
  const activeCount = (filters.active !== 'any' ? 1 : 0) + (filters.category.length > 0 ? 1 : 0) + (filters.status.length > 0 ? 1 : 0)

  // A new search or filter starts again at page 1
  useEffect(() => { setPage(1) }, [query, filters])

  useEffect(() => {
    let stale = false // ignore a slower, older response
    setLoading(true)
    const params = new URLSearchParams({ search: query, active: filters.active, page })
    filters.category.forEach(c => params.append('category', c))
    filters.status.forEach(s => params.append('status', s))
    if (filters.categoryNot) params.set('categoryNot', 'true')
    if (filters.statusNot) params.set('statusNot', 'true')

    fetch(`/api/tasks?${params}`)
      .then(res => res.json())
      .then(json => { if (!stale) { setData(json); setLoading(false) } })
    return () => { stale = true }
  }, [query, filters, page])

  return (
    <>
      <SearchToolbar query={query} onQueryChange={setQuery} placeholder="Search by title, client or ref…">
        <FiltersButton count={activeCount} open={showFilters} onToggle={() => setShowFilters(!showFilters)} />
      </SearchToolbar>

      <FilterPanel open={showFilters} onReset={() => setFilters(DEFAULTS)}>
        <FilterRow label="Active" htmlFor="taskActive">
          <YesNoAny id="taskActive" value={filters.active} onChange={active => set({ active })} />
        </FilterRow>
        <FilterRow label="Category">
          <IsCheckboxes name="category" options={data.categories} selected={filters.category}
                        onChange={category => set({ category })}
                        isNot={filters.categoryNot} onIsNotChange={categoryNot => set({ categoryNot })} />
        </FilterRow>
        <FilterRow label="Status">
          <IsCheckboxes name="status" options={STATUSES} selected={filters.status} label={statusLabel}
                        onChange={status => set({ status })}
                        isNot={filters.statusNot} onIsNotChange={statusNot => set({ statusNot })} />
        </FilterRow>
      </FilterPanel>

      <p className="text-muted mb-2">{data.total.toLocaleString()} task{data.total === 1 ? '' : 's'}</p>
      <SearchTable
        columns={columns}
        rows={data.items}
        loading={loading}
        onRowClick={row => window.location = `/Task/Details/${row.id}`}
        emptyMessage="No tasks found."
      />
      <ListPager page={data.page ?? page} pages={data.pages} onPage={setPage} />
    </>
  )
}

const el = document.getElementById('task-search-root')
if (el) createRoot(el).render(<TaskSearch />)
