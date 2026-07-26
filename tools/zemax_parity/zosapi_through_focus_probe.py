"""Inspect the OpticStudio ZOS-API Fourier through-focus MTF interface."""

from __future__ import annotations

import json
from pathlib import Path

from zosapi_export import (
    DEFAULT_ZEMAX_DIRECTORY,
    DEFAULT_ZMX_PATH,
    ensure_no_existing_instance,
    load_zosapi,
)


def public_properties(value):
    return sorted(
        {
            str(item.Name)
            for item in value.GetType().GetProperties()
            if item.GetMethod is not None and item.GetMethod.IsPublic
        }
    )


def main() -> int:
    ZOSAPI = load_zosapi(DEFAULT_ZEMAX_DIRECTORY)
    ensure_no_existing_instance()
    connection = ZOSAPI.ZOSAPI_Connection()
    application = None
    analysis = None
    try:
        application = connection.CreateNewApplication()
        system = application.PrimarySystem
        system.LoadFile(str(Path(DEFAULT_ZMX_PATH)), False)
        method_names = sorted(
            {
                str(method.Name)
                for method in system.Analyses.GetType().GetMethods()
                if "focus" in str(method.Name).lower()
                or "mtf" in str(method.Name).lower()
            }
        )
        payload = {"analysisMethods": method_names}
        factory = next(
            (
                name
                for name in method_names
                if "fft" in name.lower()
                and "focus" in name.lower()
                and name.startswith("New_")
            ),
            None,
        )
        if factory is None:
            return 2
        analysis = getattr(system.Analyses, factory)()
        settings = analysis.GetSettings()
        payload["factory"] = factory
        payload["settingProperties"] = public_properties(settings)
        payload["defaults"] = {}
        for property_name in public_properties(settings):
            try:
                value = getattr(settings, property_name)
                if hasattr(value, "GetFieldNumber"):
                    value = value.GetFieldNumber()
                elif hasattr(value, "GetWavelengthNumber"):
                    value = value.GetWavelengthNumber()
                elif hasattr(value, "GetSurfaceNumber"):
                    value = value.GetSurfaceNumber()
                payload["defaults"][property_name] = str(value)
            except Exception as error:
                payload["defaults"][property_name] = f"<error:{error}>"
        output_path = (
            Path(__file__).parents[2]
            / "artifacts"
            / "zemax"
            / "123456-fft-through-focus-probe.json"
        )
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        print(f"output={output_path}")
        return 0
    finally:
        if analysis is not None:
            try:
                analysis.Close()
            except Exception:
                pass
        if application is not None:
            try:
                application.CloseApplication()
            except Exception:
                pass


if __name__ == "__main__":
    raise SystemExit(main())
