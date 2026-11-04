using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Lifecycle implementation for one Bird resource type token.
/// </summary>
internal interface IBirdResource
{
    Task<CheckResponse> Check(CheckRequest request, CancellationToken ct);

    Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct);

    Task<CreateResponse> Create(CreateRequest request, CancellationToken ct);

    Task<ReadResponse> Read(ReadRequest request, CancellationToken ct);

    Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct);

    Task Delete(DeleteRequest request, CancellationToken ct);
}
