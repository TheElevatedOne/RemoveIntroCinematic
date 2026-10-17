# Paths can be overridden: make install PROFILE_DIR=/path/to/r2modman/profile
RORDIR      ?= $(HOME)/.local/share/Steam/steamapps/common/Risk of Rain 2
PROFILE_DIR ?= $(HOME)/.config/r2modmanPlus-local/RiskOfRain2/profiles/Default
CONFIG      ?= Release

NAMESPACE := $(shell bash -c 'source ./thunderstore.conf && echo $$NAMESPACE')
NAME      := $(shell bash -c 'source ./thunderstore.conf && echo $$NAME')
VERSION   := $(shell bash -c 'source ./thunderstore.conf && echo $$VERSION')

DLL         := bin/$(CONFIG)/$(NAME).dll
INSTALL_DIR := $(PROFILE_DIR)/BepInEx/plugins/$(NAMESPACE)-$(NAME)

.PHONY: all build install uninstall package clean

all: build

build:
	dotnet build -c $(CONFIG) -p:Version=$(VERSION) -p:RoR2Dir="$(RORDIR)" -p:ProfileDir="$(PROFILE_DIR)"

install: build
	mkdir -p "$(INSTALL_DIR)"
	cp "$(DLL)" icon.png "$(INSTALL_DIR)/"
	@echo "Installed to $(INSTALL_DIR)"

uninstall:
	rm -rf "$(INSTALL_DIR)"

package: build
	./package.sh "$(DLL)"

clean:
	dotnet clean -c $(CONFIG) >/dev/null
	rm -rf bin obj dist
