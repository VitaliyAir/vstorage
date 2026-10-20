using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.Admin;
using VStorage.Application.Features.Buckets;

namespace VStorage.Api.Endpoints
{
    /// <summary>
    /// Admin API (client role vstorage-admin), used through Scalar. Manages buckets and members of ANY owner;
    /// it never gives access to object contents.
    /// </summary>
    internal static class AdminEndpoints
    {
        public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
        {
            var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(AuthPolicies.PlatformAdmin);

            admin.MapGet("/users", SearchUsers).WithSummary("Find users and service accounts").Produces<AdminUserDto[]>();

            admin.MapGet("/buckets", ListBuckets).WithSummary("All buckets (optional exact name / owner)").Produces<AdminBucketDto[]>();
            admin.MapPost("/buckets", CreateBucket).WithSummary("Create a bucket for any principal").Produces<BucketDto>(StatusCodes.Status201Created);
            admin.MapGet("/buckets/{bucketId:guid}", GetBucket).WithSummary("Bucket details").Produces<BucketDto>();
            admin.MapPatch("/buckets/{bucketId:guid}", UpdateQuota).WithSummary("Change the quota").Produces<BucketDto>();
            admin.MapPost("/buckets/{bucketId:guid}/transfer", Transfer).WithSummary("Transfer to any principal").Produces<BucketDto>();
            admin.MapDelete("/buckets/{bucketId:guid}", DeleteBucket).WithSummary("Delete the bucket");

            admin.MapGet("/buckets/{bucketId:guid}/members", ListMembers).WithSummary("Owner and members").Produces<MemberDto[]>();
            admin.MapPost("/buckets/{bucketId:guid}/members", AddMember).WithSummary("Add a member").Produces<MemberDto>(StatusCodes.Status201Created);
            admin.MapPatch("/buckets/{bucketId:guid}/members/{userId:guid}", ChangeRole).WithSummary("Change a role").Produces<MemberDto>();
            admin.MapDelete("/buckets/{bucketId:guid}/members/{userId:guid}", RemoveMember).WithSummary("Remove a member");

            return api;
        }

        private static async Task<IResult> SearchUsers(string? search, int? limit, AdminService admin, CancellationToken cancellationToken) =>
            TypedResults.Ok(await admin.SearchUsersAsync(search, limit, cancellationToken));

        private static async Task<IResult> ListBuckets(string? name, Guid? ownerId, int? limit, AdminService admin, CancellationToken cancellationToken) =>
            TypedResults.Ok(await admin.ListBucketsAsync(name, ownerId, limit, cancellationToken));

        private static async Task<IResult> CreateBucket(
            AdminCreateBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken)
        {
            var result = await buckets.CreateAsync(Admin(http), request.OwnerId, request.Name, request.QuotaBytes, cancellationToken);
            return result.IsSuccess
                ? TypedResults.Created($"/api/v1/admin/buckets/{result.Value.Id}", result.Value)
                : result.Error.ToProblem();
        }

        private static async Task<IResult> GetBucket(Guid bucketId, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.GetAsync(Admin(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> UpdateQuota(
            Guid bucketId, UpdateBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.SetQuotaAsync(Admin(http), bucketId, request.QuotaBytes, cancellationToken)).ToHttp();

        private static async Task<IResult> Transfer(
            Guid bucketId, TransferBucketRequest request, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.TransferAsync(Admin(http), bucketId, request.NewOwnerId, cancellationToken)).ToHttp();

        private static async Task<IResult> DeleteBucket(Guid bucketId, HttpContext http, BucketService buckets, CancellationToken cancellationToken) =>
            (await buckets.DeleteAsync(Admin(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> ListMembers(Guid bucketId, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.ListAsync(Admin(http), bucketId, cancellationToken)).ToHttp();

        private static async Task<IResult> AddMember(
            Guid bucketId, AddMemberRequest request, HttpContext http, MemberService members, CancellationToken cancellationToken)
        {
            var result = await members.AddAsync(Admin(http), bucketId, request, cancellationToken);
            return result.IsSuccess
                ? TypedResults.Created($"/api/v1/admin/buckets/{bucketId}/members/{result.Value.UserId}", result.Value)
                : result.Error.ToProblem();
        }

        private static async Task<IResult> ChangeRole(
            Guid bucketId, Guid userId, ChangeMemberRoleRequest request, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.ChangeRoleAsync(Admin(http), bucketId, userId, request, cancellationToken)).ToHttp();

        private static async Task<IResult> RemoveMember(
            Guid bucketId, Guid userId, HttpContext http, MemberService members, CancellationToken cancellationToken) =>
            (await members.RemoveAsync(Admin(http), bucketId, userId, cancellationToken)).ToHttp();

        private static BucketAccess Admin(HttpContext http) => BucketAccess.AsAdmin(http.RequireCurrentPrincipal());
    }
}
