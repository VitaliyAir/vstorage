using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using VStorage.Api.Auth;
using VStorage.Api.Http;
using VStorage.Application.Features.Objects;
using VStorage.Application.Options;
using VStorage.Domain.Common;

namespace VStorage.Api.Endpoints
{
    /// <summary>Object listing, upload, download, metadata, visibility and delete (spec §5.6, §9.3).</summary>
    internal static class ObjectEndpoints
    {
        private const int MaxFieldChars = 2048;
        private const long MultipartOverheadBytes = 64 * 1024;
        private static readonly string[] GetAndHead = [HttpMethods.Get, HttpMethods.Head];

        private static readonly Error ExpectedMultipart = Error.Validation("expected-multipart", "Content-Type must be multipart/form-data with a boundary.");
        private static readonly Error FieldTooLong = Error.Validation("field-too-long", $"Form fields are limited to {MaxFieldChars} characters.");
        private static readonly Error FieldOrder = Error.Validation("field-order", "The fields 'key' and 'visibility' must come before the file.");
        private static readonly Error MissingFile = Error.Validation("missing-file", "The form has no file.");

        public static RouteGroupBuilder MapObjectEndpoints(this RouteGroupBuilder api)
        {
            var inBucket = api.MapGroup("/buckets/{bucketId:guid}/objects").WithTags("Objects");
            inBucket.MapGet("", List)
                .WithSummary("List visible objects (shared + own private): ?prefix=&delimiter=&cursor=&limit=1..1000 (default 100)")
                .Produces<ObjectListing>();
            inBucket.MapPut("", UploadRaw)
                .WithSummary("Upload (raw body): ?key=&visibility=shared|private; If-None-Match / If-Match; Content-Disposition for the file name")
                .Produces<ObjectDto>(StatusCodes.Status201Created)
                .Produces<ObjectDto>();
            inBucket.MapPost("", UploadMultipart)
                .WithSummary("Upload (multipart/form-data): fields key, visibility, then one file")
                .Produces<ObjectDto>(StatusCodes.Status201Created)
                .Produces<ObjectDto>();
            inBucket.MapMethods("/content", GetAndHead, DownloadByKey)
                .WithSummary("Download by key (own private first, then shared); ?disposition=inline for safe types");

            var objects = api.MapGroup("/objects").WithTags("Objects");
            objects.MapGet("/{objectId:guid}", GetMetadata).WithSummary("Object metadata").Produces<ObjectDto>();
            objects.MapMethods("/{objectId:guid}/content", GetAndHead, DownloadById).WithSummary("Download by id; ?disposition=inline for safe types");
            objects.MapPatch("/{objectId:guid}", ChangeVisibility).WithSummary("private → shared (object owner)").Produces<ObjectDto>();
            objects.MapDelete("/{objectId:guid}", Delete).WithSummary("Delete (own object, or another user's shared object as owner/admin)");

            return api;
        }

        private static async Task<IResult> List(
            Guid bucketId, string? prefix, string? delimiter, string? cursor, int? limit, HttpContext http, ObjectListingService listing,
            CancellationToken cancellationToken) =>
            (await listing.ListAsync(
                http.RequireCurrentPrincipal(), bucketId, new ListObjectsRequest(prefix, delimiter, cursor, limit), cancellationToken)).ToHttp();

        private static async Task<IResult> UploadRaw(
            Guid bucketId, string? key, string? visibility, HttpContext http, UploadService uploads,
            IOptions<StorageOptions> storage, CancellationToken cancellationToken)
        {
            UploadRequest.AllowBodySize(http, storage.Value.MaxUploadBytes);
            var precondition = UploadRequest.ParsePrecondition(http.Request.Headers);
            if (precondition.IsFailure)
            {
                return precondition.Error.ToProblem();
            }

            var command = new UploadCommand(
                bucketId,
                key,
                visibility,
                http.Request.ContentType,
                UploadRequest.FileName(http.Request.Headers.ContentDisposition.ToString()),
                precondition.Value,
                http.Request.ContentLength,
                http.Request.Body);

            return ToResponse(await uploads.UploadAsync(http.RequireCurrentPrincipal(), command, cancellationToken), http);
        }

        private static async Task<IResult> UploadMultipart(
            Guid bucketId, HttpContext http, UploadService uploads, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
        {
            UploadRequest.AllowBodySize(http, storage.Value.MaxUploadBytes + MultipartOverheadBytes);
            var precondition = UploadRequest.ParsePrecondition(http.Request.Headers);
            if (precondition.IsFailure)
            {
                return precondition.Error.ToProblem();
            }

            if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
                || !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase)
                || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
            {
                return ExpectedMultipart.ToProblem();
            }

            // Streamed section by section: the file is never buffered in memory or on disk by the framework.
            var reader = new MultipartReader(boundary, http.Request.Body);
            string? key = null;
            string? visibility = null;
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                {
                    continue;
                }

                if (disposition.IsFileDisposition())
                {
                    if (key is null)
                    {
                        return FieldOrder.ToProblem();
                    }

                    var command = new UploadCommand(
                        bucketId, key, visibility, section.ContentType, UploadRequest.FileName(disposition), precondition.Value, null, section.Body);
                    return ToResponse(await uploads.UploadAsync(http.RequireCurrentPrincipal(), command, cancellationToken), http);
                }

                if (disposition.IsFormDisposition())
                {
                    var value = await UploadRequest.ReadFieldAsync(section.Body, MaxFieldChars, cancellationToken);
                    if (value is null)
                    {
                        return FieldTooLong.ToProblem();
                    }

                    switch (HeaderUtilities.RemoveQuotes(disposition.Name).Value)
                    {
                        case "key":
                            key = value;
                            break;
                        case "visibility":
                            visibility = value;
                            break;
                    }
                }
            }

            return MissingFile.ToProblem();
        }

        private static async Task<IResult> DownloadByKey(
            Guid bucketId, string? key, string? visibility, string? disposition, HttpContext http, ObjectReadService objects,
            CancellationToken cancellationToken)
        {
            var content = await objects.OpenByKeyAsync(http.RequireCurrentPrincipal(), bucketId, key, visibility, cancellationToken);
            return content.IsSuccess ? content.Value.ToFileResult(http, disposition) : content.Error.ToProblem();
        }

        private static async Task<IResult> DownloadById(
            Guid objectId, string? disposition, HttpContext http, ObjectReadService objects, CancellationToken cancellationToken)
        {
            var content = await objects.OpenByIdAsync(http.RequireCurrentPrincipal(), objectId, cancellationToken);
            return content.IsSuccess ? content.Value.ToFileResult(http, disposition) : content.Error.ToProblem();
        }

        private static async Task<IResult> GetMetadata(Guid objectId, HttpContext http, ObjectReadService objects, CancellationToken cancellationToken) =>
            (await objects.GetMetadataAsync(http.RequireCurrentPrincipal(), objectId, cancellationToken)).ToHttp();

        private static async Task<IResult> ChangeVisibility(
            Guid objectId, ChangeVisibilityRequest request, HttpContext http, ObjectCommandService commands, CancellationToken cancellationToken) =>
            (await commands.ChangeVisibilityAsync(http.RequireCurrentPrincipal(), objectId, request, cancellationToken)).ToHttp();

        private static async Task<IResult> Delete(Guid objectId, HttpContext http, ObjectCommandService commands, CancellationToken cancellationToken) =>
            (await commands.DeleteAsync(http.RequireCurrentPrincipal(), objectId, cancellationToken)).ToHttp();

        // 201 for a new key, 200 for an identical existing object or an overwrite; ETag = SHA-256 (FR-O5).
        private static IResult ToResponse(Result<UploadOutcome> result, HttpContext http)
        {
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var stored = result.Value.Object;
            http.Response.Headers.ETag = $"\"{stored.Sha256}\"";
            return result.Value.Created
                ? TypedResults.Created($"/api/v1/objects/{stored.Id}", stored)
                : TypedResults.Ok(stored);
        }
    }
}
