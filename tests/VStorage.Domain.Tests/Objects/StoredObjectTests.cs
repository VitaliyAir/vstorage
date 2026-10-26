using VStorage.Domain.Events;
using VStorage.Domain.Objects;

namespace VStorage.Domain.Tests.Objects
{
    public sealed class StoredObjectTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid Bucket = Guid.NewGuid();
        private static readonly Guid Owner = Guid.NewGuid();
        private static readonly Guid Other = Guid.NewGuid();
        private static readonly string Sha = new('0', 64);

        [Fact]
        public void BeginUpload_creates_pending_object_with_defaults()
        {
            var storedObject = Begin(Visibility.Private, contentType: null, fileName: @"C:\docs\report.pdf");

            storedObject.Status.Should().Be(ObjectStatus.Pending);
            storedObject.ContentType.Should().Be(StoredObject.DefaultContentType);
            storedObject.OriginalFileName.Should().Be("report.pdf");
            storedObject.IsVisibleTo(Owner).Should().BeFalse();
            storedObject.DomainEvents.Should().BeEmpty();
        }

        [Theory]
        [InlineData("text")]
        [InlineData("text/")]
        [InlineData("/plain")]
        [InlineData("text/plain\n")]
        public void BeginUpload_rejects_bad_content_type(string contentType)
        {
            StoredObject.BeginUpload(Bucket, Key("a"), Owner, Visibility.Shared, contentType, null, Now)
                .Error.Should().Be(ObjectErrors.InvalidContentType);
        }

        [Fact]
        public void BeginUpload_accepts_content_type_with_parameters()
        {
            StoredObject.BeginUpload(Bucket, Key("a"), Owner, Visibility.Shared, "text/plain; charset=utf-8", null, Now)
                .IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void Complete_makes_object_available_and_raises_ObjectCreated()
        {
            var storedObject = Begin(Visibility.Shared);

            storedObject.Complete(42, Sha, Now).IsSuccess.Should().BeTrue();

            storedObject.Status.Should().Be(ObjectStatus.Available);
            storedObject.Size.Should().Be(42);
            storedObject.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ObjectCreated>();
            storedObject.Complete(42, Sha, Now).Error.Should().Be(ObjectErrors.NotPending);
        }

        [Theory]
        [InlineData(-1, "0000000000000000000000000000000000000000000000000000000000000000")]
        [InlineData(1, "ABCDEF0000000000000000000000000000000000000000000000000000000000")]
        [InlineData(1, "abc")]
        public void Complete_validates_size_and_hash(long size, string sha)
        {
            Begin(Visibility.Shared).Complete(size, sha, Now).Error.Should().Be(ObjectErrors.InvalidContent);
        }

        [Fact]
        public void Visibility_rules()
        {
            var shared = Available(Visibility.Shared);
            var privateObject = Available(Visibility.Private);

            shared.IsVisibleTo(Other).Should().BeTrue();
            privateObject.IsVisibleTo(Owner).Should().BeTrue();
            privateObject.IsVisibleTo(Other).Should().BeFalse();
        }

        [Fact]
        public void MakeShared_only_from_private()
        {
            var privateObject = Available(Visibility.Private);
            privateObject.ClearDomainEvents();

            privateObject.MakeShared(Now).IsSuccess.Should().BeTrue();

            privateObject.Visibility.Should().Be(Visibility.Shared);
            privateObject.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ObjectVisibilityChanged>();
            privateObject.MakeShared(Now).Error.Should().Be(ObjectErrors.AlreadyShared);
        }

        [Fact]
        public void Delete_hides_object_and_raises_ObjectDeleted_once()
        {
            var storedObject = Available(Visibility.Shared);
            storedObject.ClearDomainEvents();

            storedObject.Delete(Now).IsSuccess.Should().BeTrue();

            storedObject.Status.Should().Be(ObjectStatus.Deleted);
            storedObject.IsVisibleTo(Owner).Should().BeFalse();
            storedObject.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ObjectDeleted>();
            storedObject.Delete(Now).IsFailure.Should().BeTrue();
        }

        [Fact]
        public void Abandon_marks_pending_upload_deleted_without_event()
        {
            var storedObject = Begin(Visibility.Shared);

            storedObject.Abandon(Now);

            storedObject.Status.Should().Be(ObjectStatus.Deleted);
            storedObject.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public void Rename_moves_the_download_name_along_when_it_was_the_last_key_segment()
        {
            var storedObject = Available(Visibility.Private);

            storedObject.Rename(Key("photos/dog.png"), Now).Value.Should().BeTrue();

            storedObject.OriginalFileName.Should().Be("dog.png");
            storedObject.DomainEvents.OfType<ObjectRenamed>().Should().ContainSingle();
        }

        [Fact]
        public void Rename_keeps_a_download_name_that_differs_from_the_key()
        {
            var storedObject = StoredObject.BeginUpload(Bucket, Key("photos/cat.png"), Owner, Visibility.Private, "image/png", "original.png", Now).Value;
            storedObject.Complete(1, Sha, Now);

            storedObject.Rename(Key("photos/dog.png"), Now);

            storedObject.OriginalFileName.Should().Be("original.png");
        }

        private static ObjectKey Key(string value) => ObjectKey.Create(value).Value;

        private static StoredObject Begin(Visibility visibility, string? contentType = "image/png", string? fileName = "cat.png") =>
            StoredObject.BeginUpload(Bucket, Key("photos/cat.png"), Owner, visibility, contentType, fileName, Now).Value;

        private static StoredObject Available(Visibility visibility)
        {
            var storedObject = Begin(visibility);
            storedObject.Complete(1, Sha, Now);
            return storedObject;
        }
    }
}
