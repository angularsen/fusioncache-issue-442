#!/bin/bash
proj=RedisReproApi/RedisReproApi.csproj
dotnet build -c Release $proj || exit 1

# Propagate ctrl-c to children jobs
trap 'kill $(jobs -p)' INT

# Run 3 API instances in parallel
APP_INSTANCE='api:5800' dotnet run -c Release --no-build --project $proj -- --urls="http://*:5800" &
APP_INSTANCE='api:5801' dotnet run -c Release --no-build --project $proj -- --urls="http://*:5801" &
APP_INSTANCE='api:5802' dotnet run -c Release --no-build --project $proj -- --urls="http://*:5802" &

# Wait for all jobs to complete
wait