/**
 * shared/SearchToolbar.jsx
 *
 * A flex toolbar with a search input on the left.
 * Pass extra controls (dropdowns, toggles) as children — they render to the right of the input.
 *
 * Usage:
 *   <SearchToolbar
 *     query={query}
 *     onQueryChange={setQuery}
 *     placeholder="Search by name…"
 *   >
 *     <select ...>...</select>
 *     <ToggleSwitch ... />
 *   </SearchToolbar>
 */
export function SearchToolbar({ query, onQueryChange, placeholder = 'Search…', children }) {
  return (
    <div className="d-flex gap-2 mb-3 mt-3 align-items-center flex-wrap">
      <input
        type="search"
        className="form-control"
        style={{ maxWidth: '360px' }}
        placeholder={placeholder}
        value={query}
        onChange={e => onQueryChange(e.target.value)}
      />
      {children}
    </div>
  )
}

/**
 * A labelled toggle switch, for use inside SearchToolbar.
 *
 * Usage:
 *   <ToggleSwitch id="showInactive" label="Show inactive" checked={x} onChange={setX} />
 */
export function ToggleSwitch({ id, label, checked, onChange }) {
  return (
    <div className="form-check form-switch d-flex align-items-center ms-2 text-nowrap">
      <input
        className="form-check-input me-2"
        type="checkbox"
        id={id}
        checked={checked}
        onChange={e => onChange(e.target.checked)}
      />
      <label className="form-check-label" htmlFor={id}>
        {label}
      </label>
    </div>
  )
}
