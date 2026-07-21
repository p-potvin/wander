using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Wander.Core.Models;

namespace Wander.Core.Data
{
    public class StateDatabase
    {
        private readonly string _connectionString;

        public StateDatabase(string dbPath)
        {
            // Pooling off: connections are short-lived per call, and pooled handles keep
            // the db file locked on Windows (blocks cleanup and cross-node test isolation).
            _connectionString = $"Data Source={dbPath};Pooling=False";
        }

        public async Task InitializeAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS FileStates (
                    Guid TEXT PRIMARY KEY,
                    RelativePath TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    LastModified TEXT NOT NULL,
                    Hash TEXT NOT NULL,
                    IsDeleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_FileStates_RelativePath ON FileStates (RelativePath);

                CREATE TABLE IF NOT EXISTS FileVersions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Guid TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    Hash TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ModifiedUtc TEXT NOT NULL,
                    SourceNode TEXT NOT NULL,
                    RecordedUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_FileVersions_Guid ON FileVersions (Guid);";

            await connection.ExecuteAsync(createTableQuery);
        }

        public async Task<FileState?> GetFileStateAsync(string relativePath)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = "SELECT * FROM FileStates WHERE RelativePath = @RelativePath;";
            return await connection.QuerySingleOrDefaultAsync<FileState>(query, new { RelativePath = relativePath });
        }

        public async Task<FileState?> GetFileStateByGuidAsync(string guid)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = "SELECT * FROM FileStates WHERE Guid = @Guid;";
            return await connection.QuerySingleOrDefaultAsync<FileState>(query, new { Guid = guid });
        }

        public async Task UpsertFileStateAsync(FileState state)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = @"
                INSERT INTO FileStates (Guid, RelativePath, SizeBytes, LastModified, Hash, IsDeleted)
                VALUES (@Guid, @RelativePath, @SizeBytes, @LastModified, @Hash, @IsDeleted)
                ON CONFLICT(Guid) DO UPDATE SET
                    RelativePath = excluded.RelativePath,
                    SizeBytes = excluded.SizeBytes,
                    LastModified = excluded.LastModified,
                    Hash = excluded.Hash,
                    IsDeleted = excluded.IsDeleted;";
            
            await connection.ExecuteAsync(query, state);
        }

        public async Task<IEnumerable<FileState>> GetAllStatesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = "SELECT * FROM FileStates;";
            return await connection.QueryAsync<FileState>(query);
        }

        public async Task MarkDeletedAsync(string guid, System.DateTime whenUtc)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = @"UPDATE FileStates SET IsDeleted = 1, LastModified = @WhenUtc WHERE Guid = @Guid;";
            await connection.ExecuteAsync(query, new { Guid = guid, WhenUtc = whenUtc });
        }

        /// <summary>Hard-removes a state row (used by same-path duplicate merge — not a tombstone,
        /// so it does not propagate as a delete).</summary>
        public async Task DeleteStateAsync(string guid)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM FileStates WHERE Guid = @Guid;", new { Guid = guid });
        }

        /// <summary>Moves a file's version history from one GUID to another (duplicate merge).</summary>
        public async Task ReassignVersionsAsync(string fromGuid, string toGuid)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(
                "UPDATE FileVersions SET Guid = @To WHERE Guid = @From;",
                new { From = fromGuid, To = toGuid });
        }

        // --- Version history ---

        public async Task<long> AddVersionAsync(FileVersion version)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = @"
                INSERT INTO FileVersions (Guid, RelativePath, Hash, SizeBytes, ModifiedUtc, SourceNode, RecordedUtc)
                VALUES (@Guid, @RelativePath, @Hash, @SizeBytes, @ModifiedUtc, @SourceNode, @RecordedUtc);
                SELECT last_insert_rowid();";
            return await connection.ExecuteScalarAsync<long>(query, version);
        }

        /// <summary>Newest first.</summary>
        public async Task<IReadOnlyList<FileVersion>> GetVersionsForGuidAsync(string guid)
        {
            using var connection = new SqliteConnection(_connectionString);
            var query = "SELECT * FROM FileVersions WHERE Guid = @Guid ORDER BY RecordedUtc DESC, Id DESC;";
            return (await connection.QueryAsync<FileVersion>(query, new { Guid = guid })).AsList();
        }

        public async Task DeleteVersionAsync(long id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM FileVersions WHERE Id = @Id;", new { Id = id });
        }

        public async Task<bool> IsHashReferencedAsync(string hash)
        {
            using var connection = new SqliteConnection(_connectionString);
            var count = await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(1) FROM FileVersions WHERE Hash = @Hash;", new { Hash = hash });
            return count > 0;
        }
    }
}
