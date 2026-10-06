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
