# Build the BepInEx plugin (a .NET DLL) on Linux and stage a Thunderstore zip.
# The DLL is a managed assembly. BepInEx loads that, not a native .so.

ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
BUILD := $(ROOT)/build
DIST := $(ROOT)/dist
STAGE := $(DIST)/package

STUB_PROJ := $(ROOT)/src/UnityEngineStub/UnityEngineStub.csproj
PLUGIN_PROJ := $(ROOT)/src/RemoveIntroCinematic/RemoveIntroCinematic.csproj
STUB_DLL := $(BUILD)/stub/UnityEngine.dll
PLUGIN_DLL := $(BUILD)/RemoveIntroCinematic/RemoveIntroCinematic.dll
DIST_DLL := $(DIST)/RemoveIntroCinematic.dll

MANIFEST := $(ROOT)/thunderstore/manifest.json
VERSION := $(shell python3 -c 'import json; print(json.load(open("$(MANIFEST)"))["version_number"])')
ZIP := $(DIST)/RemoveIntroCinematic-$(VERSION).zip

DOTNET ?= dotnet

.PHONY: all dll package clean

all: dll

dll: $(DIST_DLL)

$(STUB_DLL): $(ROOT)/src/UnityEngineStub/Stub.cs $(STUB_PROJ) $(ROOT)/Directory.Build.props
	$(DOTNET) build "$(STUB_PROJ)" -c Release -o "$(BUILD)/stub" --nologo

$(PLUGIN_DLL): $(ROOT)/src/RemoveIntroCinematic/Plugin.cs $(ROOT)/src/RemoveIntroCinematic/Boot.cs $(PLUGIN_PROJ) $(STUB_DLL) $(ROOT)/Directory.Build.props
	$(DOTNET) build "$(PLUGIN_PROJ)" -c Release -o "$(BUILD)/RemoveIntroCinematic" --nologo -p:UnityEngineDll="$(STUB_DLL)"

$(DIST_DLL): $(PLUGIN_DLL)
	mkdir -p "$(DIST)"
	cp "$(PLUGIN_DLL)" "$(DIST_DLL)"

package: $(DIST_DLL)
	rm -rf "$(STAGE)"
	mkdir -p "$(STAGE)"
	cp "$(MANIFEST)" "$(ROOT)/thunderstore/README.md" "$(ROOT)/thunderstore/CHANGELOG.md" "$(STAGE)/"
	cp "$(DIST_DLL)" "$(STAGE)/RemoveIntroCinematic.dll"
	@if [ ! -f "$(ROOT)/thunderstore/icon.png" ]; then \
		printf '%s\n' "Place a 256x256 PNG at thunderstore/icon.png, then run make package again."; \
		printf '%s\n' "Everything else is staged in $(STAGE)."; \
		exit 1; \
	fi
	cp "$(ROOT)/thunderstore/icon.png" "$(STAGE)/icon.png"
	rm -f "$(ZIP)"
	python3 -c 'import pathlib, zipfile, sys; stage, dest = map(pathlib.Path, sys.argv[1:]); \
z = zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED); \
[z.write(p, p.name) for p in sorted(stage.iterdir()) if p.is_file()]; \
z.close()' "$(STAGE)" "$(ZIP)"
	@printf '%s\n' "Wrote $(ZIP)"

clean:
	rm -rf "$(BUILD)" "$(DIST)"
