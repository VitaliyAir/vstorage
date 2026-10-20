using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.Buckets;

namespace VStorage.Api.Endpoints
{
    /// <summary>Buckets and members for owners and members (spec §9.2). A missing role answers 404, not 403.</summary>
    internal static class BucketEndpoints
    {
        public static RouteGroupBuilder MapBucketEndpoints(this RouteGroupBuilder api)
        {
            var buckets = api.MapGroup("/buckets").WithTags("Buckets");

            buckets.MapPost("", Create)
                .RequireAuthorization(AuthPolicies.BucketCreator)
                .WithSummary("Create a bucket owned by the caller (client role bucket-creator)")
                .Produces<BucketDto>(StatusCodes.Status201Created);
            buckets.MapGet("", List).WithSummary("Buckets where the caller is owner or member").Produces<BucketDto[]>();
            buckets.MapGet("/{bucketId:guid}", Get).WithSummary("Bucket details").Produces<BucketDto>();
            buckets.MapPatch("/{bucketId:guid}", UpdateQuota).WithSummary("Change the quota (owner)").Produces<BucketDto>();
            buckets.MapDelete("/{bucketId:guid}", Delete).WithSummary("Delete the bucket (owner)");
            buckets.MapPost("/{bucketId:guid}/transfer", Transfer).WithSummary("Transfer ownership to a member (owner)").Produces<BucketDto>();

            var members = buckets.MapGroup("/{bucketId:guid}/members").WithTags("Members");
            members.MapGet("", ListMembers).WithSummary("Owner and members").Produces<MemberDto[]>();
            members.MapPost("", AddMember).WithSummary("Add an existing user").Produces<MemberDto>(StatusCodes.Status201Created);
            members.MapDelete("/me", Leave).WithSummary("Leave the bucket");
            members.MapPatch("/{userId:guid}", ChangeRole).WithSummary("Change a member's role").Produces<MemberDto>();
            members.MapDelete("/{userId:guid}", RemoveMember).WithSummary("Remove a member");

            return api;
        }

        private static async Task<IResult> Create(CreateBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken)
        {
            var caller = http.RequireCurrentPrincipal();
            var result = await buckets.CreateAsync(BucketAccess.AsMember(caller), caller.Id, request.Name, request.QuotaBytes, cancellationToken);
            return result.IsSuccess
                ? TypedResults.Created($"/api/v1/buckets/{result.Value.Id}", result.Value)
                : result.Error.ToProblem();
        }

        private static async Task<IResult> List(HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            TypedResults.Ok(await buckets.ListMineAsync(http.RequireCurrentPrincipal(), cancellationToken));

        private static async Task<IResult> Get(Guid bucketId, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.GetAsync(Member(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> UpdateQuota(
            Guid bucketId, UpdateBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.SetQuotaAsync(Member(http), bucketId, request.QuotaBytes, cancellationToken)).ToHttp();

        private static async Task<IResult> Delete(Guid bucketId, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.DeleteAsync(Member(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> Transfer(
            Guid bucketId, TransferBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.TransferAsync(Member(http), bucketId, request.NewOwnerId, cancellationToken)).ToHttp();

        private static async Task<IResult> ListMembers(Guid bucketId, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.ListAsync(Member(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> AddMember(
            Guid bucketId, AddMemberRequest request, HttpContext http, MemberService members, CancellationToken cancellationToken)
        {
            var result = await members.AddAsync(Member(http), bucketId, request, cancellationToken);
            return result.IsSuccess
                ? TypedResults.Created($"/api/v1/buckets/{bucketId}/members/{result.Value.UserId}", result.Value)
                : result.Error.ToProblem();
        }

        private static async Task<IResult> Leave(Guid bucketId, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.LeaveAsync(http.RequireCurrentPrincipal(), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> ChangeRole(
            Guid bucketId, Guid userId, ChangeMemberRoleRequest request, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.ChangeRoleAsync(Member(http), bucketId, userId, request, cancellationToken)).ToHttp();

        private static async Task<IResult> RemoveMember(
            Guid bucketId, Guid userId, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.RemoveAsync(Member(http), bucketId, userId, cancellationToken)).ToHttp();

        private static BucketAccess Member(HttpContext http) => BucketAccess.AsMember(http.RequireCurrentPrincipal());
    }
}
