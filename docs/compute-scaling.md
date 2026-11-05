# Cloud Run and Neon compute policies

`GcpCloudRunCompute.CpuIdle` controls CPU allocation between requests. Leave it null
to preserve Google's default, set it true for request-based allocation, or set it
false for continuous CPU allocation. An always-running worker needs both
`MinCount = 1` and `CpuIdle = false`; `CpuLimit = "1"` gives each instance one vCPU.
For an environment that may scale to zero, use `MinCount = 0` and `CpuIdle = true`.
Google bills continuous CPU allocation even while the instance has no requests.

`NeonPostgresOptions.MinCu`, `MaxCu`, and `SuspendTimeoutSeconds` apply to the
project's existing default read/write compute. Use `MinCu = MaxCu = 0.25` for a
fixed quarter-CU compute. A suspend timeout of 300 allows scale-to-zero after five
minutes of inactivity; -1 disables suspension and requires a paid Neon plan. Zero
uses Neon's five-minute default. Null leaves a setting unchanged.

The Neon provider's project defaults do not update the live compute, so the library
updates that endpoint through the native `crosscloud-neon` provider
(`NeonEndpointSettings`, see [native provider plugins](provider-plugins.md)). It needs the
API key supplied to `NeonCloudResourceFactory` (or `neon:apiKey` when constructing the
database without a provider). It sends only the settings that differ, waits until Neon
reports them, and never creates or deletes endpoints; removing the settings leaves the
compute as it is. `NeonPostgresDatabase.ComputeSettings` exposes the confirmed policy as
JSON, and `EndpointSettings` is the resource itself. `pulumi refresh` reports changes made
in the Neon console, and the next `pulumi up` restores the requested values.
