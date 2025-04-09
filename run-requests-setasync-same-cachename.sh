#!/bin/bash

# Propagate ctrl-c to children jobs
trap 'kill $(jobs -p)' INT

concurrency=10
maxSeconds=30
timeoutMsPerRequest=30000

echo "Begin 3x loadtest /repro/setAsync?useRandomCacheName=false (timeouts expected due to multiple instances using same cache name)"
echo "- APIs: localhost:5800, localhost:5801, localhost:5802"
echo "- Max test duration: $maxSeconds seconds"
echo "- Concurrency per API: $concurrency"
echo "- Timeout per request: $timeoutMsPerRequest ms"
echo ""

# Mute pesky Node warning spam
export NODE_NO_WARNINGS=1

# Run against single API
# npx --yes loadtest --maxSeconds 10 --timeout 5000 --concurrency 2 http://localhost:5800/repro/setAsync?useRandomCacheName=false

# Run against 3 API instances in parallel
npx --yes loadtest --maxSeconds $maxSeconds --timeout $timeoutMsPerRequest --concurrency $concurrency http://localhost:5800/repro/setAsync?useRandomCacheName=false &
npx --yes loadtest --maxSeconds $maxSeconds --timeout $timeoutMsPerRequest --concurrency $concurrency http://localhost:5801/repro/setAsync?useRandomCacheName=false &
npx --yes loadtest --maxSeconds $maxSeconds --timeout $timeoutMsPerRequest --concurrency $concurrency http://localhost:5802/repro/setAsync?useRandomCacheName=false &

# Wait for all jobs to complete
wait