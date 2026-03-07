import { useState, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { SearchToolbar } from '../shared/SearchToolbar'
import { SearchTable } from '../shared/SearchTable'

const STATUSES = ['Open', 'InProgress', 'Blocked', 'Completed', 'Cancelled']

const PRIORITY_BADGES = {
  Low:    'bg-secondary',
  Normal: 'bg-primary',
  High:   'bg-warning text-dark',
  Urgent: 'bg-danger',
}

const STATUS_BADGES = {
  Open:       'bg-info text-dark',
  InProgress: 'bg-primary',
  Blocked:    'bg-warning text-dark',
  Completed:  'bg-success',
  Cancelled:  'bg-secondary',
}

function DueDate({ iso, status }) {
  if (!iso) return <span className="text-muted">—</span>
  const due = new Date(iso)
  const overdue = due < new Date()
    && status !== 'Completed'
    && status !== 'Cancelled'
  return (
    <span className={overdue ? 'text-danger fw-semibold' : ''}>
      {due.toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}
    </span>
  )
}

const columns = [
  { key: 'title',       label: 'Title',    render: row => <span className="fw-semibold">{row.title}</span> },
  { key: 'clientName',  label: 'Client' },
  { key: 'siteAddress', label: 'Property' },
  {
    key: 'priority',
    label: 'Priority',
    render: row => (
      <span className={`badge ${PRIORITY_BADGES[row.priority] ?? 'bg-secondary'}`}>
        {row.priority}
      </span>
    )
  },
  {
    key: 'status',
    label: 'Status',
    render: row => (
      <span className={`badge ${STATUS_BADGES[row.status] ?? 'bg-secondary'}`}>
        {row.status === 'InProgress' ? 'In Progress' : row.status}
      </span>
    )
  },
  {
    key: 'dueDateUtc',
    label: 'Due',
    render: row => <DueDate iso={row.dueDateUtc} status={row.status} />
  },
  {
    key: 'actions',
    label: '',
    render: row => (
      <a
        href={`/Task/Details/${row.id}`}
        className="btn btn-sm btn-outline-primary"
        onClick={e => e.stopPropagation()}
      >
        Details
      </a>
    )
  }
]

function TaskSearch() {
  const [query, setQuery]   = useState('')
  const [status, setStatus] = useState('')
  const [tasks, setTasks]   = useState([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setLoading(true)
    const params = new URLSearchParams()
    if (query)  params.set('search', query)
    if (status) params.set('status', status)

    fetch(`/api/tasks?${params}`)
      .then(res => res.json())
      .then(data => { setTasks(data); setLoading(false) })
  }, [query, status])

  return (
    <>
      <SearchToolbar query={query} onQueryChange={setQuery} placeholder="Search by title or client…">
        <select
          className="form-select w-auto"
          value={status}
          onChange={e => setStatus(e.target.value)}
        >
          <option value="">All statuses</option>
          {STATUSES.map(s => (
            <option key={s} value={s}>{s === 'InProgress' ? 'In Progress' : s}</option>
          ))}
        </select>
      </SearchToolbar>
      <SearchTable
        columns={columns}
        rows={tasks}
        loading={loading}
        onRowClick={row => window.location = `/Task/Details/${row.id}`}
        emptyMessage="No tasks found."
      />
    </>
  )
}

const el = document.getElementById('task-search-root')
if (el) createRoot(el).render(<TaskSearch />)
