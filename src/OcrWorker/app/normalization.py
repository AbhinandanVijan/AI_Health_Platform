from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal
from typing import Optional


@dataclass(frozen=True)
class NormalizedReading:
    biomarker_code: str
    source_name: str
    value: Decimal
    unit: str
    normalized_value: Optional[Decimal]
    normalized_unit: Optional[str]


_BIOMARKER_ALIASES = {
    "glucose": "GLUCOSE",
    "blood glucose": "GLUCOSE",
    "fasting glucose": "GLUCOSE",
    "hba1c": "HBA1C",
    "a1c": "HBA1C",
    "hemoglobin a1c": "HBA1C",
    "total cholesterol": "TOTAL_CHOLESTEROL",
    "cholesterol": "TOTAL_CHOLESTEROL",
    "hdl": "HDL",
    "ldl": "LDL",
    "triglycerides": "TRIGLYCERIDES",
    "hemoglobin": "HEMOGLOBIN",
    "wbc": "WBC",
    "rbc": "RBC",
}


def canonicalize_biomarker(name: str) -> str:
    key = " ".join(name.strip().lower().split())
    if key in _BIOMARKER_ALIASES:
        return _BIOMARKER_ALIASES[key]

    for alias, code in _BIOMARKER_ALIASES.items():
        if alias in key:
            return code

    return key.upper().replace(" ", "_")


def normalize_unit(raw_unit: str) -> str:
    unit = (
        raw_unit.strip()
        .replace("μ", "u")
        .replace("µ", "u")
        .replace("×", "x")
        .replace("³", "^3")
        .replace("⁶", "^6")
    )
    compact = unit.lower().replace(" ", "")

    mapping = {
        "mg/dl": "mg/dL",
        "mmol/l": "mmol/L",
        "g/dl": "g/dL",
        "%": "%",
        "x10^3/ul": "x10^3/uL",
        "10^3/ul": "x10^3/uL",
        "x10^6/ul": "x10^6/uL",
        "10^6/ul": "x10^6/uL",
    }
    return mapping.get(compact, unit)


def to_canonical_value(biomarker_code: str, value: Decimal, unit: str) -> tuple[Optional[Decimal], Optional[str]]:
    canonical_unit = normalize_unit(unit)

    if biomarker_code == "GLUCOSE":
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("18.0")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code in {"TOTAL_CHOLESTEROL", "HDL", "LDL"}:
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("38.67")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code == "TRIGLYCERIDES":
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("88.57")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code == "HBA1C":
        if canonical_unit == "%":
            return value, "%"

    if biomarker_code == "HEMOGLOBIN":
        if canonical_unit == "g/dL":
            return value, "g/dL"

    if biomarker_code == "WBC":
        if canonical_unit == "x10^3/uL":
            return value, "x10^3/uL"

    if biomarker_code == "RBC":
        if canonical_unit == "x10^6/uL":
            return value, "x10^6/uL"

    return None, None


def normalize_reading(source_name: str, value: Decimal, unit: str) -> NormalizedReading:
    biomarker_code = canonicalize_biomarker(source_name)
    display_unit = normalize_unit(unit)
    normalized_value, normalized_unit = to_canonical_value(biomarker_code, value, display_unit)

    return NormalizedReading(
        biomarker_code=biomarker_code,
        source_name=source_name,
        value=value,
        unit=display_unit,
        normalized_value=normalized_value,
        normalized_unit=normalized_unit,
    )
