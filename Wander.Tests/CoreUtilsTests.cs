using Wander.Core.Utils;
using Xunit;

namespace Wander.Tests
{
    public class HashHelperTests
    {
        [Fact]
        public void ComputesStableSha256()
        {
            using var dir = new TempDir();
            var path = dir.WriteFile("a.txt", "hello wander");

            var first = HashHelper.ComputeFileHash(path);
            var second = HashHelper.ComputeFileHash(path);

            Assert.Equal(first, second);
            Assert.Equal(64, first.Length); // sha256 hex
        }

        [Fact]
        public void MissingFileYieldsEmpty()
        {
            Assert.Equal(string.Empty, HashHelper.ComputeFileHash(@"Z:\does\not\exist.bin"));
        }
    }

    public class PathUtilsTests
    {
        [Fact]
        public void NormalizesToForwardSlashes()
        {
            Assert.Equal("docs/notes/todo.md", PathUtils.NormalizeRelative(@"docs\notes\todo.md"));
        }

        [Fact]
        public void RoundTripsLocalAndRelative()
        {
            using var dir = new TempDir();
            var local = PathUtils.ToLocalPath(dir.Path, "docs/readme.md");
            Assert.Equal("docs/readme.md", PathUtils.ToRelativePath(dir.Path, local));
        }

        [Fact]
        public void RejectsPathTraversal()
        {
            using var dir = new TempDir();
            Assert.Throws<InvalidOperationException>(() => PathUtils.ToLocalPath(dir.Path, "../../evil.exe"));
        }

        [Theory]
        [InlineData(".wander", true)]
        [InlineData(".wander/trash/x.txt", true)]
        [InlineData(".Wander/state.db", true)]
        [InlineData("docs/.wanderlust.md", false)]
        [InlineData("notes.txt", false)]
        public void IdentifiesInternalPaths(string relative, bool expected)
        {
            Assert.Equal(expected, PathUtils.IsInternal(relative));
        }
    }

    public class ConflictNamingTests
    {
        [Fact]
        public void NamesConflictWithNodeAndTimestamp()
        {
            using var dir = new TempDir();
            var original = dir.WriteFile("report.docx", "x");
            var when = new DateTime(2026, 7, 16, 14, 30, 0, DateTimeKind.Utc);

            var conflict = ConflictNaming.BuildConflictPath(original, "Phils-laptop", when);

            Assert.EndsWith("report (conflict — Phils-laptop, 2026-07-16 14.30).docx", conflict);
        }

        [Fact]
        public void SanitizesInvalidFilenameCharacters()
        {
            using var dir = new TempDir();
            var original = dir.WriteFile("a.txt", "x");

            var conflict = ConflictNaming.BuildConflictPath(original, "bad:node/name", DateTime.UtcNow);

            Assert.DoesNotContain(':', Path.GetFileName(conflict));
            Assert.DoesNotContain('/', Path.GetFileName(conflict));
        }

        [Fact]
        public void CountersWhenConflictNameTaken()
        {
            using var dir = new TempDir();
            var original = dir.WriteFile("a.txt", "x");
            var when = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);

            var first = ConflictNaming.BuildConflictPath(original, "node", when);
            File.WriteAllText(first, "taken");
            var second = ConflictNaming.BuildConflictPath(original, "node", when);

            Assert.NotEqual(first, second);
            Assert.Contains("#2", second);
        }
    }
}
