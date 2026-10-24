import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'
import {
  FiltersButton, FilterPanel, FilterRow, YesNoAny, IsCheckboxes, ListPager,
  useSelection, selectColumn, SelectionBar, Modal, postJson, SuccessAlert, filtersOpenAtFirst
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

// Defaults match Uptick's Tasks page: active tasks, any category, any status.
// technician: '' any, 'none' unassigned, 'me' the signed-in technician's tasks, else an Uptick user id.
// Technicians open the list on their own tasks (defaultTechnician 'me').
const defaultsFor = defaultTechnician => ({
  active: 'yes', category: [], categoryNot: false, status: [], statusNot: false,
  dueFrom: '', dueTo: '', technician: defaultTechnician
})

// Filters from the page's address, so other pages can link to a filtered list
// (e.g. Routines → Generate tasks → "Assign them": /Task?category=I%26T&dueFrom=…&dueTo=…&technician=none)
function filtersFromUrl(defaults) {
  const q = new URLSearchParams(window.location.search)
  return {
    query: q.get('search') ?? '',
    filters: {
      ...defaults,
      active: q.get('active') ?? defaults.active,
      category: q.getAll('category'),
      categoryNot: q.get('categoryNot') === 'true',
      status: q.getAll('status'),
      statusNot: q.get('statusNot') === 'true',
      dueFrom: q.get('dueFrom') ?? '',
      dueTo: q.get('dueTo') ?? '',
      technician: q.get('technician') ?? defaults.technician
    }
  }
}

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

// Who's doing it: the technician, an Uptick user (a dropdown for admins, to assign straight from the list),
// and who Uptick has an imported task assigned to when that's someone else
function Technician({ row, canEdit, techs, onAssign }) {
  const uptick = row.uptickTechnician && (
    <div className="small text-muted" title="Assigned in Uptick">{row.uptickTechnician} · Uptick</div>
  )
  if (!canEdit) {
    return (
      <>
        {row.assignedToName ?? (row.uptickTechnician ? null : <span className="text-muted">Unassigned</span>)}
        {uptick}
      </>
    )
  }
  const current = row.assignedToId == null ? '' : String(row.assignedToId)
  // Someone outside the list (e.g. office staff Uptick assigned it to) still shows as assigned
  const others = current && !techs.some(t => t.id === current) ? [{ id: current, name: row.assignedToName }] : []
  return (
    <div onClick={e => e.stopPropagation()}>
      <select className="form-select form-select-sm" style={{ minWidth: '11rem' }} aria-label={`Technician for ${row.displayRef ?? 'task'}`}
              value={current} onChange={e => onAssign(row, e.target.value)}>
        <option value="">Unassigned</option>
        {[...others, ...techs].map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
      </select>
      {uptick}
    </div>
  )
}

function filterParams(query, filters) {
  const params = new URLSearchParams({ search: query, active: filters.active })
  filters.category.forEach(c => params.append('category', c))
  filters.status.forEach(s => params.append('status', s))
  if (filters.categoryNot) params.set('categoryNot', 'true')
  if (filters.statusNot) params.set('statusNot', 'true')
  if (filters.dueFrom) params.set('dueFrom', filters.dueFrom)
  if (filters.dueTo) params.set('dueTo', filters.dueTo)
  if (filters.technician) params.set('technician', filters.technician)
  return params
}

// The filters as the bulk endpoint takes them (no empty dates, which wouldn't parse)
function filterBody(query, filters) {
  return {
    search: query, ...filters,
    dueFrom: filters.dueFrom || null, dueTo: filters.dueTo || null, technician: filters.technician || null
  }
}

function TaskSearch({ canEdit, techs, canFilterMine, defaultTechnician, onlyMine }) {
  // onlyMine: a technician, who only ever sees their own tasks (the server holds them to it), has no technician filter
  const [DEFAULTS] = useState(() => defaultsFor(defaultTechnician))
  const [initial] = useState(() => filtersFromUrl(DEFAULTS))
  const [query, setQuery]     = useState(initial.query)
  const [filters, setFilters] = useState(initial.filters)
  const [page, setPage]       = useState(1)
  const [showFilters, setShowFilters] = useState(filtersOpenAtFirst)
  const [data, setData]       = useState({ total: 0, pages: 1, items: [], categories: [] })
  const [loading, setLoading] = useState(true)
  const [reload, setReload]   = useState(0)
  const [editing, setEditing] = useState(false)
  const [bulkAction, setBulkAction] = useState('assign')
  const [newStatus, setNewStatus]   = useState('Completed')
  const [technicianId, setTechnicianId] = useState('')
  const [busy, setBusy]       = useState(false)
  const [message, setMessage] = useState(null)

  const set = patch => setFilters(f => ({ ...f, ...patch }))
  const activeCount = (filters.active !== 'any' ? 1 : 0) + (filters.category.length > 0 ? 1 : 0)
    + (filters.status.length > 0 ? 1 : 0) + (filters.dueFrom || filters.dueTo ? 1 : 0) + (filters.technician !== DEFAULTS.technician ? 1 : 0)
  const selection = useSelection(data.items, data.total, `${query}|${JSON.stringify(filters)}|${page}|${reload}`)

  // A new search or filter starts again at page 1, and the address keeps the filters (back button, sharing)
  useEffect(() => {
    setPage(1)
    const params = filterParams(query, filters)
    if (!query) params.delete('search')
    // A technician who picks "Any value" keeps it on reload, rather than going back to their own tasks
    if (!filters.technician && DEFAULTS.technician) params.set('technician', '')
    window.history.replaceState(null, '', `${window.location.pathname}?${params}`)
  }, [query, filters])

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
        ids: selection.ids, allMatching: selection.allMatching, ...filterBody(query, filters),
        bulkAction, newStatus, technicianId
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

  // One task, from its row's dropdown. The row updates in place; the list isn't reloaded, so it doesn't
  // vanish from an "Unassigned" filter while the admin works down the page.
  async function assignOne(row, id) {
    try {
      await postJson('/api/tasks/bulk', {
        ids: [row.id], allMatching: false, ...filterBody(query, { ...DEFAULTS, technician: '' }), active: 'any',
        bulkAction: 'assign', technicianId: id
      })
      const name = techs.find(t => t.id === id)?.name ?? (String(row.assignedToId) === id ? row.assignedToName : null)
      setData(d => ({
        ...d,
        items: d.items.map(i => i.id === row.id ? { ...i, assignedToId: id ? Number(id) : null, assignedToName: id ? name : null } : i)
      }))
      setMessage(id ? `${row.displayRef ?? 'Task'} assigned to ${name}.` : `${row.displayRef ?? 'Task'} unassigned.`)
    } catch (err) {
      alert(err.message)
    }
  }

  const columns = [
    { key: 'displayRef', label: 'Ref', render: row => <a className="text-nowrap" href={`/Task/Details/${row.id}`} onClick={e => e.stopPropagation()}>{row.displayRef ?? '—'}</a> },
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
    { key: 'dueDateUtc',  label: 'Due', render: row => <DueDate iso={row.dueDateUtc} status={row.status} /> },
    { key: 'technician',  label: 'Technician', render: row => <Technician row={row} canEdit={canEdit} techs={techs} onAssign={assignOne} /> }
  ]

  const noTechs = canEdit && techs.length === 0 && (
    <p className="small text-muted mb-0">
      No technicians yet. A technician is anyone whose FyreApp login has the Tech role: give them one on
      the <a href="/Users">Users</a> page.
    </p>
  )

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
        <FilterRow label="Due" htmlFor="taskDueFrom">
          <input id="taskDueFrom" type="date" className="form-control w-auto" aria-label="Due from"
                 value={filters.dueFrom} onChange={e => set({ dueFrom: e.target.value })} />
          <span className="text-muted">to</span>
          <input type="date" className="form-control w-auto" aria-label="Due to"
                 value={filters.dueTo} onChange={e => set({ dueTo: e.target.value })} />
        </FilterRow>
        {!onlyMine && (
          <FilterRow label="Technician" htmlFor="taskTechFilter">
            <select id="taskTechFilter" className="form-select" value={filters.technician} onChange={e => set({ technician: e.target.value })}>
              <option value="">Any value</option>
              {canFilterMine && <option value="me">My tasks</option>}
              <option value="none">Unassigned</option>
              {techs.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </FilterRow>
        )}
      </FilterPanel>

      <SelectionBar selection={selection} total={data.total} noun="Tasks" nounSingular="Task" canEdit={canEdit}
                    onEdit={() => setEditing(true)} downloadUrl={`/Task/Download?${filterParams(query, filters)}`} />

      <SearchTable
        columns={canEdit ? [selectColumn(selection), ...columns] : columns}
        rows={data.items}
        loading={loading}
        onRowClick={row => window.location = `/Task/Details/${row.id}`}
        emptyMessage={data.hidden > 0 ? (
          // Found, but the filters hide them (often a task inactive in Uptick, with Active "Yes")
          <span>
            No tasks found with these filters, but {data.hidden.toLocaleString()} task{data.hidden === 1 ? '' : 's'} matching
            “{query}” {data.hidden === 1 ? 'is' : 'are'} hidden by them.{' '}
            <button type="button" className="btn btn-link btn-sm p-0 align-baseline"
                    onClick={() => setFilters({ ...DEFAULTS, active: 'any' })}>
              Show {data.hidden === 1 ? 'it' : 'them'}
            </button>
          </span>
        ) : 'No tasks found.'}
      />
      <ListPager page={data.page ?? page} pages={data.pages} onPage={setPage} />

      <Modal title={`Edit ${selection.count.toLocaleString()} Tasks`} open={editing} busy={busy}
             onClose={() => setEditing(false)} onSubmit={apply}>
        <p>You have selected {selection.count.toLocaleString()} tasks. What do you want to do?</p>

        <div className="form-check">
          <input className="form-check-input" type="radio" name="taskBulkAction" id="task-assign"
                 checked={bulkAction === 'assign'} onChange={() => setBulkAction('assign')} />
          <label className="form-check-label" htmlFor="task-assign">Assign technician</label>
        </div>
        {bulkAction === 'assign' && (
          <div className="border rounded p-3 my-2">
            <label className="form-label" htmlFor="taskTech">Technician</label>
            <select id="taskTech" className="form-select" value={technicianId} onChange={e => setTechnicianId(e.target.value)}>
              <option value="">— Unassigned —</option>
              {techs.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
            {noTechs}
          </div>
        )}

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
              Completing a routine task moves its routines on, as completing it on its own page does.
            </p>
          </div>
        )}
      </Modal>

      {noTechs}
    </>
  )
}

const el = document.getElementById('task-search-root')
if (el) createRoot(el).render(
  <TaskSearch canEdit={el.dataset.canEdit === 'true'} techs={JSON.parse(el.dataset.techs || '[]')}
              canFilterMine={el.dataset.canFilterMine === 'true'} defaultTechnician={el.dataset.defaultTechnician || ''}
              onlyMine={el.dataset.onlyMine === 'true'} />
)
