#!/usr/bin/env python3
"""Build the Thunderstore zip from thunderstore.toml.

thunderstore.toml is the source of truth. This writes thunderstore/manifest.json
and the zip. Editing the toml and running `make package` changes both.
"""

import json
import re
import struct
import sys
import tomllib
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOML_PATH = ROOT / "thunderstore.toml"
MANIFEST_PATH = ROOT / "thunderstore" / "manifest.json"

SEMVER = re.compile(r"^\d+\.\d+\.\d+$")
IDENT = re.compile(r"^[A-Za-z0-9_]+$")
DEPENDENCY = re.compile(r"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$")
WEBSITE = re.compile(r"^(https?://|mailto:|ipfs://)\S+$", re.IGNORECASE)
PNG = b"\x89PNG\r\n\x1a\n"


def fail(message):
    print(message, file=sys.stderr)
    sys.exit(1)


def require(condition, message):
    if not condition:
        fail(message)


def resolve(path_text):
    path = Path(path_text)
    if not path.is_absolute():
        path = ROOT / path
    return path.resolve()


def png_size(path):
    data = path.read_bytes()
    require(data.startswith(PNG) and len(data) >= 24, path.name + " is not a PNG.")
    require(data[12:16] == b"IHDR", path.name + " is missing an IHDR chunk.")
    return struct.unpack(">II", data[16:24])


def dependency_string(name, version):
    version = str(version).strip()
    full = name.strip() + "-" + version
    require(
        DEPENDENCY.match(full),
        "Dependency " + full + " is not team-name-version. "
        "In thunderstore.toml use [package.dependencies] with "
        'bbepis-BepInExPack = "5.4.2122".',
    )
    return full


def load_config():
    require(TOML_PATH.is_file(), "Missing " + str(TOML_PATH))
    with TOML_PATH.open("rb") as handle:
        data = tomllib.load(handle)

    package = data.get("package")
    require(isinstance(package, dict), "thunderstore.toml is missing [package].")

    namespace = str(package.get("namespace", "")).strip()
    name = str(package.get("name", "")).strip()
    version = str(package.get("versionNumber", "")).strip()
    description = str(package.get("description", "")).strip()
    website = str(package.get("websiteUrl", "") or "").strip()
    dependencies = package.get("dependencies") or {}

    require(IDENT.match(namespace), "Team name must be letters, numbers, and underscores: " + namespace)
    require(IDENT.match(name) and len(name) <= 128, "Package name is invalid: " + name)
    require(SEMVER.match(version), "versionNumber must be Major.Minor.Patch: " + version)
    require(0 < len(description) <= 250, "description must be 1 to 250 characters.")
    require(website == "" or WEBSITE.match(website), "websiteUrl must be http, https, mailto, or ipfs.")
    require(isinstance(dependencies, dict), "[package.dependencies] must be a table of name = version.")

    manifest = {
        "name": name,
        "version_number": version,
        "website_url": website,
        "description": description,
        "dependencies": [dependency_string(dep, ver) for dep, ver in dependencies.items()],
    }
    require(len(manifest["dependencies"]) <= 1000, "Thunderstore allows at most 1000 dependencies.")

    build = data.get("build") or {}
    require(isinstance(build, dict), "[build] must be a table.")
    return {
        "namespace": namespace,
        "name": name,
        "version": version,
        "manifest": manifest,
        "icon": resolve(build.get("icon") or "./thunderstore/icon.png"),
        "readme": resolve(build.get("readme") or "./thunderstore/README.md"),
        "outdir": resolve(build.get("outdir") or "./dist"),
        "copies": build.get("copy") or [],
    }


def warn_plugin_version(version):
    plugin = ROOT / "src" / "RemoveIntroCinematic" / "Plugin.cs"
    if not plugin.is_file():
        return
    match = re.search(r'PluginVersion\s*=\s*"([^"]+)"', plugin.read_text(encoding="utf-8"))
    if match and match.group(1) != version:
        print(
            "warning: PluginVersion is " + match.group(1) + " but thunderstore.toml is " + version + ".",
            file=sys.stderr,
        )


def zip_name_for(source, target):
    target = (target or "/").strip().replace("\\", "/")
    if target in ("", "/"):
        return source.name
    if target.endswith("/"):
        return target + source.name
    return target.lstrip("/")


def collect_files(config):
    icon = config["icon"]
    readme = config["readme"]
    require(icon.is_file(), "Icon not found: " + str(icon))
    width, height = png_size(icon)
    require(width == 256 and height == 256, "Icon must be 256x256. " + icon.name + " is " + str(width) + "x" + str(height) + ".")
    require(readme.is_file(), "Readme not found: " + str(readme))

    files = {
        "manifest.json": MANIFEST_PATH,
        "icon.png": icon,
        "README.md": readme,
    }

    copies = config["copies"]
    require(isinstance(copies, list), "[[build.copy]] entries must be a list.")
    for entry in copies:
        require(isinstance(entry, dict) and "source" in entry, "Each [[build.copy]] needs a source.")
        source = resolve(entry["source"])
        require(source.is_file(), "Copy source not found: " + str(source))
        arcname = zip_name_for(source, entry.get("target", "/"))
        require(arcname not in files or files[arcname] == source, "Two files want the zip path " + arcname + ".")
        if arcname == "manifest.json":
            continue
        files[arcname] = source

    require("RemoveIntroCinematic.dll" in files, "The package is missing RemoveIntroCinematic.dll. Build it before packaging.")
    return files


def main():
    config = load_config()
    warn_plugin_version(config["version"])

    text = json.dumps(config["manifest"], indent=2) + "\n"
    MANIFEST_PATH.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST_PATH.write_text(text, encoding="utf-8")

    files = collect_files(config)
    outdir = config["outdir"]
    outdir.mkdir(parents=True, exist_ok=True)
    zip_path = outdir / (config["namespace"] + "-" + config["name"] + "-" + config["version"] + ".zip")

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for arcname in sorted(files):
            archive.write(files[arcname], arcname)

    names = ", ".join(sorted(files))
    deps = ", ".join(config["manifest"]["dependencies"]) or "(none)"
    print("Wrote " + str(MANIFEST_PATH))
    print("Wrote " + str(zip_path))
    print("website_url=" + config["manifest"]["website_url"])
    print("dependencies=" + deps)
    print("files=" + names)


if __name__ == "__main__":
    main()
