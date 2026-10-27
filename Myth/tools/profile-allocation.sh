#!/bin/bash -xe
export CFG=Debug 
export APP_NAME=MYTH 
export TRACE_PROFILE=gc-verbose 
dotnet build -c $CFG && dotnet trace collect --format Chromium --profile $TRACE_PROFILE -- bin/$CFG/$APP_NAME "$@" && open https://ui.perfetto.dev
