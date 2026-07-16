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
                CREATE INDEX IF NOT EXISTS IX_FileStates_RelativePath ON FileStates (RelativePath);";

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
    }
}
