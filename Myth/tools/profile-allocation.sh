CFG=Debug APP_NAME=MYTH TRACE_PROFILE=gc-verbose \
    dotnet build -c $CFG && \
    dotnet trace collect --format Chromium --profile $TRACE_PROFILE -- bin/$CFG/net10.0/$APP_NAME && \
    open https://ui.perfetto.dev
