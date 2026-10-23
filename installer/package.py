"""Builds a self-contained Tickbird download for one platform.

Usage (from the repository root):  python installer/package.py <rid> [--out artifacts/release]

  rid   win-x64 | linux-x64 | osx-arm64 | osx-x64

Output (named with the version from Version.props):
  win-x64     Tickbird-<version>-win-x64.zip          (unzip and run Tickbird.Desktop.exe)
  linux-x64   Tickbird-<version>-linux-x64.tar.gz     (includes a .desktop launcher and icon)
  osx-*       Tickbird-<version>-<rid>.zip            (contains Tickbird.app, not code-signed)

The Windows installer (MSI / Setup.exe) is built separately by installer/build-installer.ps1.
Works on any OS: publishing is a normal `dotnet publish`, and the archives are written here so the
executable permissions are right even when this runs on Windows.
"""
import argparse
import io
import os
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT = os.path.join(ROOT, "src", "Tickbird.Desktop", "Tickbird.Desktop.csproj")
ASSETS = os.path.join(ROOT, "src", "Tickbird.Desktop", "Assets")
EXECUTABLE = "Tickbird.Desktop"
RIDS = ("win-x64", "linux-x64", "osx-arm64", "osx-x64")


def version():
    with open(os.path.join(ROOT, "Version.props"), encoding="utf-8-sig") as f:
        return re.search(r"<Version>([^<]+)</Version>", f.read()).group(1)


def publish(rid, folder):
    shutil.rmtree(folder, ignore_errors=True)
    subprocess.run(
        ["dotnet", "publish", PROJECT, "-c", "Release", "-r", rid, "--self-contained", "true",
         "-p:DebugType=None", "-p:DebugSymbols=false", "-o", folder],
        check=True)


def files(folder):
    for base, _, names in os.walk(folder):
        for name in sorted(names):
            full = os.path.join(base, name)
            yield full, os.path.relpath(full, folder).replace(os.sep, "/")


def is_executable(relative, rid):
    return (rid.startswith("win") and relative.endswith(".exe")) or relative == EXECUTABLE or relative.endswith(".sh")


def write_zip(target, entries, prefix=""):
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for full, relative, executable in entries:
            info = zipfile.ZipInfo(prefix + relative)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = ((0o755 if executable else 0o644) | stat.S_IFREG) << 16
            with open(full, "rb") as f:
                archive.writestr(info, f.read())


def package_windows(rid, folder, target):
    write_zip(target, [(full, rel, False) for full, rel in files(folder)], prefix="Tickbird/")


def package_linux(rid, folder, target):
    desktop = (
        "[Desktop Entry]\nType=Application\nName=Tickbird\n"
        "Comment=Client for time series databases\nExec=sh -c '\"$(dirname \"$(readlink -f \"%k\")\")/Tickbird.Desktop\"'\n"
        "Icon=tickbird\nCategories=Development;Database;\nTerminal=false\n")
    with tarfile.open(target, "w:gz") as archive:
        def add_bytes(name, data, mode):
            info = tarfile.TarInfo("Tickbird/" + name)
            info.size, info.mode = len(data), mode
            archive.addfile(info, io.BytesIO(data))

        for full, relative in files(folder):
            info = archive.gettarinfo(full, "Tickbird/" + relative)
            info.mode = 0o755 if is_executable(relative, rid) else 0o644
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            with open(full, "rb") as f:
                archive.addfile(info, f)
        add_bytes("tickbird.desktop", desktop.encode("utf-8"), 0o755)
        with open(os.path.join(ASSETS, "tickbird-logo.png"), "rb") as f:
            add_bytes("tickbird.png", f.read(), 0o644)
        add_bytes("README.txt", (
            "Tickbird - run ./Tickbird.Desktop\n"
            "To add it to your application menu, copy tickbird.desktop to ~/.local/share/applications/ and edit its Exec line to the full path.\n"
        ).encode("utf-8"), 0o644)


def package_macos(rid, folder, target, ver):
    plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Tickbird</string>
  <key>CFBundleDisplayName</key><string>Tickbird</string>
  <key>CFBundleIdentifier</key><string>io.github.sadaqatbukhari.tickbird</string>
  <key>CFBundleVersion</key><string>{ver}</string>
  <key>CFBundleShortVersionString</key><string>{ver}</string>
  <key>CFBundleExecutable</key><string>{EXECUTABLE}</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
"""
    # Assemble Tickbird.app on disk so it can be signed.
    bundle = os.path.join(os.path.dirname(folder), f"Tickbird-{rid}.app")
    shutil.rmtree(bundle, ignore_errors=True)
    macos = os.path.join(bundle, "Contents", "MacOS")
    shutil.copytree(folder, macos)
    with open(os.path.join(bundle, "Contents", "Info.plist"), "w", encoding="utf-8") as f:
        f.write(plist)
    icon = os.path.join(ROOT, "installer", "macos", "AppIcon.icns")
    if os.path.exists(icon):
        os.makedirs(os.path.join(bundle, "Contents", "Resources"), exist_ok=True)
        shutil.copy(icon, os.path.join(bundle, "Contents", "Resources", "AppIcon.icns"))
    if sys.platform != "win32":
        os.chmod(os.path.join(macos, EXECUTABLE), 0o755)

    # Apple silicon refuses to start unsigned code, so sign ad hoc (no developer account needed) when on a Mac.
    if sys.platform == "darwin":
        subprocess.run(["codesign", "--force", "--deep", "--sign", "-", bundle], check=True)
    else:
        print("WARNING: not running on macOS, so Tickbird.app is NOT signed. The Apple-silicon build will not start "
              "unsigned; build the macOS downloads on a Mac (GitHub Actions does this).", file=sys.stderr)

    entries = []
    for full, relative in files(bundle):
        if sys.platform == "win32":
            executable = relative.endswith(("/" + EXECUTABLE, ".dylib")) or relative.endswith("MacOS/" + EXECUTABLE)
        else:
            executable = bool(os.stat(full).st_mode & 0o111)
        entries.append((full, relative, executable))
    write_zip(target, entries, prefix="Tickbird.app/")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("rid", choices=RIDS)
    parser.add_argument("--out", default=os.path.join("artifacts", "release"))
    args = parser.parse_args()

    ver = version()
    out = os.path.join(ROOT, args.out)
    os.makedirs(out, exist_ok=True)
    folder = os.path.join(ROOT, "artifacts", "publish", args.rid)
    publish(args.rid, folder)

    if args.rid == "win-x64":
        target = os.path.join(out, f"Tickbird-{ver}-win-x64.zip")
        package_windows(args.rid, folder, target)
    elif args.rid == "linux-x64":
        target = os.path.join(out, f"Tickbird-{ver}-linux-x64.tar.gz")
        package_linux(args.rid, folder, target)
    else:
        target = os.path.join(out, f"Tickbird-{ver}-{args.rid}.zip")
        package_macos(args.rid, folder, target, ver)
    print(f"Created {target} ({os.path.getsize(target) / 1048576:.1f} MB)")


if __name__ == "__main__":
    sys.exit(main())
