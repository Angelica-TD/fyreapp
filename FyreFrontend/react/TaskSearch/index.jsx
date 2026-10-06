import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'
import {
  FiltersButton, FilterPanel, FilterRow, YesNoAny, IsCheckboxes, ListPager,
  useSelection, selectColumn, SelectionBar, Modal, postJson, SuccessAlert
} from '../shared/ListControls'

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

function filterParams(query, filters) {
  const params = new URLSearchParams({ search: query, active: filters.active })
  filters.category.forEach(c => params.append('category', c))
  filters.status.forEach(s => params.append('status', s))
  if (filters.categoryNot) params.set('categoryNot', 'true')
  if (filters.statusNot) params.set('statusNot', 'true')
  return params
}

function TaskSearch({ canEdit, techs }) {
  const [query, setQuery]     = useState('')
  const [filters, setFilters] = useState(DEFAULTS)
  const [page, setPage]       = useState(1)
  const [showFilters, setShowFilters] = useState(true)
  const [data, setData]       = useState({ total: 0, pages: 1, items: [], categories: [] })
  const [loading, setLoading] = useState(true)
  const [reload, setReload]   = useState(0)
  const [editing, setEditing] = useState(false)
  const [bulkAction, setBulkAction] = useState('status')
  const [newStatus, setNewStatus]   = useState('Completed')
  const [techUserId, setTechUserId] = useState('')
  const [busy, setBusy]       = useState(false)
  const [message, setMessage] = useState(null)

  const set = patch => setFilters(f => ({ ...f, ...patch }))
  const activeCount = (filters.active !== 'any' ? 1 : 0) + (filters.category.length > 0 ? 1 : 0) + (filters.status.length > 0 ? 1 : 0)
  const selection = useSelection(data.items, data.total, `${query}|${JSON.stringify(filters)}|${page}|${reload}`)

  // A new search or filter starts again at page 1
  useEffect(() => { setPage(1) }, [query, filters])

  useEffect(() => {
    let stale = false // ignore a slower, older response
    setLoading(true)
    const params = filterParams(query, filters)
    params.set('page', page)
    fetch(`/api/tasks?${params}`)
      .then(res => res.json())
      .then(json => { if (!stale) { setData(json); setLoading(false) } })
    return () => { stale = true }
  }, [query, filters, page, reload])

  async function apply() {
    setBusy(true)
    try {
      const res = await postJson('/api/tasks/bulk', {
        ids: selection.ids, allMatching: selection.allMatching, search: query, ...filters,
        bulkAction, newStatus, techUserId
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

      <SelectionBar selection={selection} total={data.total} noun="Tasks" nounSingular="Task" canEdit={canEdit}
                    onEdit={() => setEditing(true)} downloadUrl={`/Task/Download?${filterParams(query, filters)}`} />

      <SearchTable
        columns={canEdit ? [selectColumn(selection), ...columns] : columns}
        rows={data.items}
        loading={loading}
        onRowClick={row => window.location = `/Task/Details/${row.id}`}
        emptyMessage="No tasks found."
      />
      <ListPager page={data.page ?? page} pages={data.pages} onPage={setPage} />

      <Modal title={`Edit ${selection.count.toLocaleString()} Tasks`} open={editing} busy={busy}
             onClose={() => setEditing(false)} onSubmit={apply}>
        <p>You have selected {selection.count.toLocaleString()} tasks. What do you want to do?</p>

        <div className="form-check">
          <input className="form-check-input" type="radio" name="taskBulkAction" id="task-status"
                 checked={bulkAction === 'status'} onChange={() => setBulkAction('status')} />
          <label className="form-check-label" htmlFor="task-status">Change status</label>
        </div>
        {bulkAction === 'status' && (
          <div className="border rounded p-3 my-2">
            <label className="form-label" htmlFor="newTaskStatus">New status</label>
            <select id="newTaskStatus" className="form-select" value={newStatus} onChange={e => setNewStatus(e.target.value)}>
              {STATUSES.map(s => <option key={s} value={s}>{statusLabel(s)}</option>)}
            </select>
            <p className="small text-muted mt-2 mb-0">
              Completing a routine task also moves its schedules on, as completing it on its own page does.
            </p>
          </div>
        )}

        <div className="form-check">
          <input className="form-check-input" type="radio" name="taskBulkAction" id="task-assign"
                 checked={bulkAction === 'assign'} onChange={() => setBulkAction('assign')} />
          <label className="form-check-label" htmlFor="task-assign">Assign technician</label>
        </div>
        {bulkAction === 'assign' && (
          <div className="border rounded p-3 my-2">
            <label className="form-label" htmlFor="taskTech">Technician</label>
            <select id="taskTech" className="form-select" value={techUserId} onChange={e => setTechUserId(e.target.value)}>
              <option value="">— Unassigned —</option>
              {techs.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </div>
        )}
      </Modal>
    </>
  )
}

const el = document.getElementById('task-search-root')
if (el) createRoot(el).render(
  <TaskSearch canEdit={el.dataset.canEdit === 'true'} techs={JSON.parse(el.dataset.techs || '[]')} />
)
