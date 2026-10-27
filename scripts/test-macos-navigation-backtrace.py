"""Cloud-only native stack capture; never creates a process-memory dump."""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import threading
import time


def main():
    if sys.platform != "darwin" or os.environ.get("GITHUB_ACTIONS") != "true":
        raise RuntimeError("Native backtraces are restricted to the temporary GitHub Mac runner.")
    parser = argparse.ArgumentParser()
    parser.add_argument("--results-directory", required=True)
    args = parser.parse_args()
    reports = Path(args.results_directory).resolve() / "native-crash"
    reports.mkdir(parents=True, exist_ok=True)
    marker = reports / "navigation-debugger.pid"
    if marker.exists() or Path(str(marker) + ".continue").exists():
        raise RuntimeError("Use a fresh diagnostic directory.")
    environment = os.environ.copy()
    environment["OPTICAL_SYSTEM_DESIGN_NAVIGATION_DEBUGGER_PID"] = str(marker)
    done = threading.Event()
    errors = []

    def capture():
        try:
            deadline = time.monotonic() + 240
            while not marker.exists():
                if done.wait(0.1) or time.monotonic() >= deadline:
                    return
            process_id = int(marker.read_text(encoding="utf-8").strip())
            command = subprocess.check_output(
                ["/bin/ps", "-p", str(process_id), "-o", "args="], text=True)
            if "testhost.dll" not in command:
                raise RuntimeError("Marker does not identify the cloud test host.")
            release = str(marker) + ".continue"
            commands = [
                "settings set auto-confirm true",
                "process handle SIGSEGV -s true -n true -p true",
                "process handle SIGBUS -s true -n true -p true",
                "process handle SIGABRT -s true -n true -p true",
                "script open(" + repr(release) + ", 'w').close()",
                "continue",
                "thread backtrace all",
                "register read pc lr fp sp",
                "process detach",
            ]
            arguments = ["sudo", "-n", "/usr/bin/lldb", "--batch", "-p", str(process_id)]
            for command in commands:
                arguments.extend(["-o", command])
            output = reports / "navigation-lldb.txt"
            with output.open("w", encoding="utf-8") as stream:
                subprocess.run(arguments, stdout=stream, stderr=subprocess.STDOUT,
                               timeout=90, check=False)
            if output.stat().st_size > 20 * 1024 * 1024:
                with output.open("r+b") as stream:
                    stream.truncate(20 * 1024 * 1024)
        except Exception as error:
            errors.append(str(error))
        finally:
            # Release the test on attach failure; its own original checks still run.
            Path(str(marker) + ".continue").touch()

    observer = threading.Thread(target=capture, daemon=True)
    observer.start()
    result = subprocess.run(
        ["pwsh", "-NoProfile", "-File", "scripts/test-lens-navigation-release.ps1",
         "-ResultsDirectory", args.results_directory], env=environment, check=False)
    done.set()
    observer.join(timeout=100)
    if observer.is_alive():
        raise RuntimeError("Native stack observer did not finish.")
    if errors:
        (reports / "navigation-debugger-error.txt").write_text("\n".join(errors), encoding="utf-8")
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
