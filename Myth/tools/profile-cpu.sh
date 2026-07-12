#!/bin/bash -xe

dotnet build -c Debug && dotnet trace collect --format Chromium -- bin/Debug/net10.0/Myth && open https://ui.perfetto.dev
