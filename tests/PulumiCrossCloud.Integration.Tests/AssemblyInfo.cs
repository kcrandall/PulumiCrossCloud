// Each fixture deploys a whole stack against its own emulator, and the GCP library keeps
// per-process static state (project config, enabled-API cache), so stacks run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
