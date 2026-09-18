SHELL := /bin/zsh

# Local convenience only: borrow the astra_terra SDK when it happens to be
# installed. CI provisions its own via actions/setup-dotnet and the runner is
# this same Mac, so never let the probe shadow it there.
ASTRA_DOTNET := /Users/lalmei/projects/astra_terra/.dotnet
ifeq ($(CI),)
  ASTRA_DOTNET_BIN := $(wildcard $(ASTRA_DOTNET)/dotnet)
endif

ifneq ($(ASTRA_DOTNET_BIN),)
  DOTNET_CLI_HOME := $(CURDIR)/.dotnet-home
  DOTNET_ENV := PATH="$(ASTRA_DOTNET):$$PATH" DOTNET_CLI_HOME="$(DOTNET_CLI_HOME)" DOTNET_CLI_TELEMETRY_OPTOUT=1
  DOTNET := $(DOTNET_ENV) $(ASTRA_DOTNET)/dotnet
else
  DOTNET_ENV := DOTNET_CLI_TELEMETRY_OPTOUT=1
  DOTNET := $(DOTNET_ENV) dotnet
endif

CONFIGURATION ?= Release
TARGET_FRAMEWORK := net10.0
GAME_APP ?= /Applications/Vintage Story.app
MODS_DIR ?= $(HOME)/Library/Application Support/VintagestoryData/Mods
DEPLOY_DIR := $(MODS_DIR)/AcervusLapidum
BUILD_OUTPUT_DIR := mod/bin/$(CONFIGURATION)/$(TARGET_FRAMEWORK)
DIST_DIR := dist
MOD_VERSION = $(shell perl -0ne 'print $$1 if /"version":\s*"([0-9]+\.[0-9]+\.[0-9]+)"/' mod/modinfo.json)
PACKAGE_FILE = $(DIST_DIR)/AcervusLapidum-$(MOD_VERSION).zip
UV ?= uv
UV_RUN := $(UV) run
# Version files are listed in .bumpversion.toml. Install once:
#   uv tool install bump-my-version
BUMP ?= bump-my-version
PART ?= patch

# The layout config is generated from the game's own stone shapes, then committed. Builds,
# packaging, and CI consume the committed file — only `make assets` reads the install.
LAYOUT_CONFIG := mod/assets/acervuslapidum/config/rockpile-layout.json

.PHONY: help assets build package deploy install run deploy-run test test-tools test-game \
        bump-version bump-version-files bump-minor-version bump-patch-version

help:
	@printf "Targets:\n"
	@printf "  make test          Run every unit test (tools + game)\n"
	@printf "  make test-tools    Unit-test the geometry generator (no game needed)\n"
	@printf "  make test-game     Compile the mod against the game DLLs\n"
	@printf "  make assets        Regenerate the rock pile layout config from the game shapes\n"
	@printf "  make build         Build from committed assets (offline)\n"
	@printf "  make package       Build and zip from committed assets (offline)\n"
	@printf "  make deploy        Bump the patch version, then install into Vintage Story Mods\n"
	@printf "  make install       Install the zip without touching the version\n"
	@printf "  make run           Launch Vintage Story\n"
	@printf "  make deploy-run    Deploy then launch\n"
	@printf "  make bump-patch-version  Increment patch version, build, and install\n"
	@printf "  make bump-minor-version  Increment minor version, reset patch to 0, build, and install\n"
	@printf "  make bump-version VERSION=0.3.0  Set an exact version, build, and install\n"

# Two suites, split by what they cover: the Python geometry that writes the layout config, and
# the C# the game runs. Both are unit tests — no world, no launching Vintage Story.
test: test-tools test-game

test-tools:
	@$(UV_RUN) python -m unittest discover -s tests -t tests

test-game:
	@$(DOTNET) build mod/AcervusLapidum.csproj -c $(CONFIGURATION) -v minimal --nologo

# Reads survival/shapes/item/stone-pile.json out of the install, so it needs the game present.
# Normal build, package, install, and CI use the committed output instead.
assets:
	@$(UV_RUN) python tools/rockpile_geometry.py --game "$(GAME_APP)" --out "$(LAYOUT_CONFIG)"

build:
	@$(DOTNET) build mod/AcervusLapidum.csproj -c $(CONFIGURATION) -v minimal

package: build
	@mkdir -p "$(DIST_DIR)"
	@rm -f "$(PACKAGE_FILE)"
	@cd "$(BUILD_OUTPUT_DIR)" && zip -qr "$(CURDIR)/$(PACKAGE_FILE)" .
	@printf "Packaged $(PACKAGE_FILE)\n"

# Every deploy ships a version nobody has seen before, so a build sitting in the Mods folder
# can never claim a number that is already tagged or published. Reach for install when you
# want the same version reinstalled — deploy always moves the patch number.
deploy: bump-patch-version

# The raw install, with the version left exactly as it is. bump-version already wrote the
# number it wants before it gets here, so it installs through this and not through deploy,
# which would bump a second time on top of it.
install: package
	@mkdir -p "$(MODS_DIR)"
	@rm -rf "$(DEPLOY_DIR)"
	@rm -f "$(MODS_DIR)"/AcervusLapidum-*.zip(N)
	@cp "$(PACKAGE_FILE)" "$(MODS_DIR)/"
	@printf "Deployed $(PACKAGE_FILE) to $(MODS_DIR)/\n"

run:
	@open -a "$(GAME_APP)"

deploy-run: deploy run

# The version lives in files listed in .bumpversion.toml. Installing rather than
# deploying is what stops deploy's own patch bump from landing on top of this one.
# Re-invoke make after rewriting the version so PACKAGE_FILE picks up the new number.
bump-version-files:
	@if [[ -n "$(VERSION)" ]]; then \
		if ! [[ "$(VERSION)" =~ ^[0-9]+\.[0-9]+\.[0-9]+$$ ]]; then printf "VERSION must look like 0.2.1\n"; exit 2; fi; \
		$(BUMP) bump --new-version "$(VERSION)"; \
	else \
		$(BUMP) bump $(PART); \
	fi
	@printf "Bumped Acervus Lapidum source version to $$($(BUMP) show current_version)\n"

bump-version:
	@if [[ -z "$(VERSION)" ]]; then printf "Usage: make bump-version VERSION=0.3.0\n"; exit 2; fi
	@$(MAKE) bump-version-files VERSION="$(VERSION)"
	@$(MAKE) install

bump-minor-version:
	@$(MAKE) bump-version-files PART=minor
	@$(MAKE) install

bump-patch-version:
	@$(MAKE) bump-version-files PART=patch
	@$(MAKE) install
