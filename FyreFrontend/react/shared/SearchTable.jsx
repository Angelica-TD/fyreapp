/**
 * shared/SearchTable.jsx
 *
 * A generic data table with built-in loading and empty states.
 *
 * Props:
 *   columns  — array of { key, label, render? }
 *              key:    used as React key and to read row[key] by default
 *              label:  column header text
 *              render: optional (row) => ReactNode for custom cell content
 *
 *   rows     — array of data objects
 *   loading  — bool
 *   onRowClick — optional (row) => void, makes rows clickable
 *   emptyMessage — optional string (default: "No results found.")
 *
 * Usage:
 *   const columns = [
 *     { key: 'name',  label: 'Client' },
 *     { key: 'phone', label: 'Phone' },
 *     { key: 'actions', label: '', render: row => <a href={...}>Details</a> }
 *   ]
 *   <SearchTable columns={columns} rows={clients} loading={loading} onRowClick={...} />
 */
export function SearchTable({ columns, rows, loading, onRowClick, emptyMessage = 'No results found.' }) {
  if (loading) {
    return <p className="text-muted">Loading…</p>
  }

  return (
    <div className="table-responsive">
      <table className="table table-hover align-middle">
        <thead>
          <tr>
            {columns.map(col => (
              <th key={col.key}>{col.label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="text-muted">{emptyMessage}</td>
            </tr>
          ) : rows.map((row, i) => (
            <tr
              key={row.id ?? i}
              style={onRowClick ? { cursor: 'pointer' } : undefined}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
            >
              {columns.map(col => (
                <td key={col.key}>
                  {col.render ? col.render(row) : row[col.key]}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
