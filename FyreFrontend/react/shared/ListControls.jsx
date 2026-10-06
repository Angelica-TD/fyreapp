import { useState, useEffect } from 'react'

/**
 * shared/ListControls.jsx
 *
 * Pieces for the Uptick-style list pages, matching the server-rendered ones (Routines, Assets, …):
 * a Filters button with a count, a labelled filter row, checkbox groups and a pager.
 */

export function FiltersButton({ count, open, onToggle }) {
  return (
    <button type="button" className="btn btn-light border text-nowrap" onClick={onToggle} aria-expanded={open}>
      <i className="bi bi-funnel"></i> Filters <span className="badge rounded-pill bg-secondary">{count}</span>
    </button>
  )
}

export function FilterPanel({ open, onReset, children }) {
  if (!open) return null
  return (
    <div className="bg-light border rounded p-3 mb-3">
      {children}
      <div className="d-flex justify-content-end">
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onReset}>Reset to defaults</button>
      </div>
    </div>
  )
}

export function FilterRow({ label, htmlFor, children }) {
  return (
    <div className="row mb-2 align-items-center">
      <label className="col-sm-3 col-form-label text-sm-end" htmlFor={htmlFor}>{label}:</label>
      <div className="col-sm-9 d-flex flex-wrap gap-2 align-items-center">{children}</div>
    </div>
  )
}

// Yes / No / Any select for a boolean filter ("yes" | "no" | "any")
export function YesNoAny({ id, value, onChange }) {
  return (
    <select id={id} className="form-select" value={value} onChange={e => onChange(e.target.value)}>
      <option value="yes">Yes</option>
      <option value="no">No</option>
      <option value="any">Any</option>
    </select>
  )
}

// Is / Is not + a group of checkboxes; nothing ticked = any value
export function IsCheckboxes({ name, options, selected, onChange, isNot, onIsNotChange, label = o => o }) {
  const toggle = value =>
    onChange(selected.includes(value) ? selected.filter(v => v !== value) : [...selected, value])
  return (
    <>
      <select className="form-select w-auto" value={isNot ? 'not' : 'is'} onChange={e => onIsNotChange(e.target.value === 'not')}
              aria-label={`${name} match`}>
        <option value="is">Is</option>
        <option value="not">Is not</option>
      </select>
      {options.map(o => (
        <div className="form-check form-check-inline mb-0" key={o}>
          <input className="form-check-input" type="checkbox" id={`${name}-${o}`}
                 checked={selected.includes(o)} onChange={() => toggle(o)} />
          <label className="form-check-label" htmlFor={`${name}-${o}`}>{label(o)}</label>
        </div>
      ))}
      {selected.length === 0 && <span className="text-muted small">Any value</span>}
    </>
  )
}

export function ListPager({ page, pages, onPage }) {
  if (pages <= 1) return null
  return (
    <nav aria-label="Pages">
      <ul className="pagination pagination-sm">
        <li className={`page-item ${page <= 1 ? 'disabled' : ''}`}>
          <button type="button" className="page-link" onClick={() => onPage(page - 1)}>Previous</button>
        </li>
        <li className="page-item disabled"><span className="page-link">Page {page} of {pages}</span></li>
        <li className={`page-item ${page >= pages ? 'disabled' : ''}`}>
          <button type="button" className="page-link" onClick={() => onPage(page + 1)}>Next</button>
        </li>
      </ul>
    </nav>
  )
}

/**
 * Selection, as on the server-rendered lists: tick rows, tick the page, or "Select all N" (every row matching
 * the filters, sent to the server as allMatching). A new search, filter or page clears it.
 */
export function useSelection(items, total, resetKey) {
  const [ids, setIds] = useState(() => new Set())
  const [allMatching, setAllMatching] = useState(false)

  useEffect(() => { setIds(new Set()); setAllMatching(false) }, [resetKey])

  const pageIds = items.map(i => i.id)
  const pageTicked = pageIds.length > 0 && pageIds.every(id => ids.has(id))
  const count = allMatching ? total : ids.size

  return {
    count,
    allMatching,
    ids: [...ids],
    has: id => allMatching || ids.has(id),
    toggle: id => {
      setAllMatching(false)
      setIds(prev => { const next = new Set(prev); next.has(id) ? next.delete(id) : next.add(id); return next })
    },
    togglePage: () => {
      setAllMatching(false)
      setIds(pageTicked ? new Set() : new Set(pageIds))
    },
    pageTicked: allMatching || pageTicked,
    pagePartly: !allMatching && !pageTicked && pageIds.some(id => ids.has(id)),
    canSelectAll: !allMatching && pageTicked && total > pageIds.length,
    selectAll: () => setAllMatching(true),
    clear: () => { setIds(new Set()); setAllMatching(false) }
  }
}

// Checkbox column for SearchTable
export function selectColumn(selection) {
  return {
    key: '__select',
    label: (
      <input type="checkbox" className="form-check-input" aria-label="Select all on this page"
             checked={selection.pageTicked}
             ref={el => { if (el) el.indeterminate = selection.pagePartly }}
             onChange={selection.togglePage} />
    ),
    render: row => (
      <input type="checkbox" className="form-check-input" aria-label="Select"
             checked={selection.has(row.id)}
             onClick={e => e.stopPropagation()}
             onChange={() => selection.toggle(row.id)} />
    )
  }
}

// Count, or Edit N / Clear selection / Select all N once something is ticked; Download (N) on the right
export function SelectionBar({ selection, total, noun, nounSingular, canEdit, onEdit, downloadUrl }) {
  const selected = selection.count
  return (
    <div className="d-flex flex-wrap align-items-center gap-2 mb-2">
      {selected === 0 || !canEdit
        ? <span className="text-muted">{total.toLocaleString()} {(total === 1 ? nounSingular : noun).toLowerCase()}</span>
        : <>
            <button type="button" className="btn btn-primary btn-sm" onClick={onEdit}>Edit {selected.toLocaleString()} {noun}</button>
            <button type="button" className="btn btn-link btn-sm" onClick={selection.clear}>Clear selection</button>
            {selection.canSelectAll &&
              <button type="button" className="btn btn-link btn-sm" onClick={selection.selectAll}>
                Select all {total.toLocaleString()} {noun.toLowerCase()}
              </button>}
          </>}
      <a className={`btn btn-link btn-sm ms-auto ${total === 0 ? 'disabled' : ''}`} href={downloadUrl}>
        <i className="bi bi-download"></i> Download ({total.toLocaleString()})
      </a>
    </div>
  )
}

// A Bootstrap-styled modal driven by React state
export function Modal({ title, open, onClose, onSubmit, submitLabel = 'Apply', busy, children }) {
  if (!open) return null
  return (
    <div className="modal d-block" tabIndex="-1" role="dialog" aria-modal="true" style={{ background: 'rgba(0,0,0,.5)' }}>
      <div className="modal-dialog modal-lg">
        <form className="modal-content" onSubmit={e => { e.preventDefault(); onSubmit() }}>
          <div className="modal-header">
            <h2 className="modal-title h5">{title}</h2>
            <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
          </div>
          <div className="modal-body">{children}</div>
          <div className="modal-footer">
            <button type="button" className="btn btn-light" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Working…' : submitLabel}</button>
          </div>
        </form>
      </div>
    </div>
  )
}

// POST JSON with the page's anti-forgery token (rendered by @Html.AntiForgeryToken() in the view)
export async function postJson(url, body) {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify(body)
  })
  const json = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(json.message ?? `Request failed (${res.status})`)
  return json
}

export function SuccessAlert({ message, onClose }) {
  if (!message) return null
  return (
    <div className="alert alert-success alert-dismissible" role="alert">
      {message}
      <button type="button" className="btn-close" aria-label="Close" onClick={onClose}></button>
    </div>
  )
}
