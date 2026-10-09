// SPDX-License-Identifier: AGPL-3.0-only
using Xunit;

// Several tests set process-wide environment variables (the host reads NEXOPBX__* at startup) while other tests start
// complete hosts. Running them in parallel makes them see each other's variables, so this assembly runs serially.
// The whole suite takes seconds; correctness beats speed here.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
