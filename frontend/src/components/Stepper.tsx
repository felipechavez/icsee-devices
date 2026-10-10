interface Props {
  value: number
  onChange: (value: number) => void
  min: number
  max: number
  step: number
  unit: string
  label: string
}

const clamp = (n: number, min: number, max: number) => Math.max(min, Math.min(max, n))

/** Campo numérico con − y +; el valor escrito se ajusta a los límites al salir del campo. */
export function Stepper({ value, onChange, min, max, step, unit, label }: Props) {
  return (
    <div className="stepper" aria-invalid={value < min || value > max}>
      <button type="button" aria-label={`Restar ${step}`} disabled={value <= min} onClick={() => onChange(clamp(value - step, min, max))}>
        −
      </button>
      <input
        type="number"
        inputMode="numeric"
        aria-label={label}
        min={min}
        max={max}
        value={Number.isNaN(value) ? '' : value}
        onChange={(e) => onChange(e.target.valueAsNumber)}
        onBlur={() => onChange(clamp(Number.isNaN(value) ? min : Math.round(value), min, max))}
      />
      <span>{unit}</span>
      <button type="button" aria-label={`Sumar ${step}`} disabled={value >= max} onClick={() => onChange(clamp(value + step, min, max))}>
        +
      </button>
    </div>
  )
}
