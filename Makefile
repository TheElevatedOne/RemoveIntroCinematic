# Build the BepInEx plugin (a .NET DLL) on Linux and stage a Thunderstore zip.
# The DLL is a managed assembly. BepInEx loads that, not a native .so.
# `make package` reads thunderstore.toml and rewrites thunderstore/manifest.json.

ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
BUILD := $(ROOT)/build
DIST := $(ROOT)/dist

STUB_PROJ := $(ROOT)/src/UnityEngineStub/UnityEngineStub.csproj
PLUGIN_PROJ := $(ROOT)/src/RemoveIntroCinematic/RemoveIntroCinematic.csproj
STUB_DLL := $(BUILD)/stub/UnityEngine.dll
PLUGIN_DLL := $(BUILD)/RemoveIntroCinematic/RemoveIntroCinematic.dll
DIST_DLL := $(DIST)/RemoveIntroCinematic.dll

DOTNET ?= dotnet

.PHONY: all dll package clean

all: package

dll: $(DIST_DLL)

$(STUB_DLL): $(ROOT)/src/UnityEngineStub/Stub.cs $(STUB_PROJ) $(ROOT)/Directory.Build.props
	$(DOTNET) build "$(STUB_PROJ)" -c Release -o "$(BUILD)/stub" --nologo

$(PLUGIN_DLL): $(ROOT)/src/RemoveIntroCinematic/Plugin.cs $(ROOT)/src/RemoveIntroCinematic/Boot.cs $(PLUGIN_PROJ) $(STUB_DLL) $(ROOT)/Directory.Build.props
	$(DOTNET) build "$(PLUGIN_PROJ)" -c Release -o "$(BUILD)/RemoveIntroCinematic" --nologo -p:UnityEngineDll="$(STUB_DLL)"

$(DIST_DLL): $(PLUGIN_DLL)
	mkdir -p "$(DIST)"
	cp "$(PLUGIN_DLL)" "$(DIST_DLL)"

package: $(DIST_DLL)
	python3 "$(ROOT)/tools/make_thunderstore.py"

clean:
	rm -rf "$(BUILD)" "$(DIST)"
